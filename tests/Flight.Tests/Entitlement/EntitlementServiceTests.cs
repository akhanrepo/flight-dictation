using Flight.Core;
using Flight.Core.Accounts;
using Flight.Core.Entitlement;
using Flight.Core.Storage;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Flight.Tests.Entitlement;

/// <summary>
/// Feature #5 acceptance criteria: the 30-day trial window, its back-dated
/// anchor, subscription checks, and the offline cache policy.
/// </summary>
public class EntitlementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemorySessionRepository _sessions = new();
    private readonly InMemoryDeviceStore _devices = new();
    private readonly FakeAccountSession _account = new();
    private readonly FakeSupabaseClient _supabase = new();
    private readonly FlightOptions _options = new();
    private readonly FakeTimeProvider _time = new(Now);

    private EntitlementService CreateService() => new(
        _sessions, _devices, _account, _supabase, _options, _time,
        NullLogger<EntitlementService>.Instance);

    private void AddCloudDictation(DateTimeOffset at) =>
        _sessions.Sessions.Add(new DictationSession
        {
            RawTranscript = "raw",
            CleanedOutput = "clean",
            LlmMode = "cloud",
            CreatedAt = at,
        });

    private void AddLocalDictation(DateTimeOffset at) =>
        _sessions.Sessions.Add(new DictationSession
        {
            RawTranscript = "raw",
            CleanedOutput = "clean",
            LlmMode = "local",
            CreatedAt = at,
        });

    private async Task SignInAsync() => await _account.SignInAsync("ali@example.com", "password");

    private void CacheEntitlement(string entitlement, DateTimeOffset syncedAt) =>
        _devices.UpdateAsync(_devices.Current with
        {
            AccountId = "account-1",
            CachedEntitlement = entitlement,
            LastSyncedAt = syncedAt,
        }).Wait();

    // Criterion: with no cloud history the trial clock hasn't started, and
    // cloud is available without an account
    [Fact]
    public async Task NoCloudHistory_Allows_Cloud_With_No_Account()
    {
        AddLocalDictation(Now.AddDays(-100));

        var decision = await CreateService().EvaluateAsync();

        Assert.True(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.TrialNotStarted, decision.Status);
        Assert.Null(_account.Current);
    }

    // Criterion: the trial-start date is the earliest cloud row, including
    // rows that predate this feature shipping
    [Fact]
    public async Task TrialStart_Is_The_Earliest_Cloud_Dictation()
    {
        AddCloudDictation(Now.AddDays(-20));
        AddCloudDictation(Now.AddDays(-40));
        AddCloudDictation(Now.AddDays(-5));

        var decision = await CreateService().EvaluateAsync();

        // Anchored at -40, so the window closed 10 days ago — not restarted
        // from the most recent row, and not from when this feature shipped.
        Assert.False(decision.CloudAllowed);
        Assert.Equal(Now.AddDays(-40).AddDays(30), decision.TrialEndsAt);
    }

    // Criterion: while within 30 days of trial start, cloud works with no
    // account required
    [Fact]
    public async Task WithinTrial_Allows_Cloud_With_No_Account_And_No_Backend_Call()
    {
        AddCloudDictation(Now.AddDays(-29));

        var decision = await CreateService().EvaluateAsync();

        Assert.True(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.TrialActive, decision.Status);
        Assert.Null(_account.Current);
        Assert.Equal(0, _supabase.EntitlementCallCount);
    }

    // Criterion: once 30 days past trial start with no subscription, cloud is
    // blocked with a clear upgrade prompt — not silently denied
    [Fact]
    public async Task ExpiredTrial_Without_Account_Blocks_With_An_Upgrade_Prompt()
    {
        AddCloudDictation(Now.AddDays(-31));

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.TrialExpired, decision.Status);
        Assert.True(decision.OffersUpgrade);
        Assert.NotEmpty(decision.Message);
        Assert.Contains("local mode", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    // The boundary itself: the trial is inclusive of day 30, exclusive after
    [Fact]
    public async Task TrialBoundary_Is_Exactly_Thirty_Days()
    {
        AddCloudDictation(Now.AddDays(-30).AddSeconds(1));
        Assert.True((await CreateService().EvaluateAsync()).CloudAllowed);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False((await CreateService().EvaluateAsync()).CloudAllowed);
    }

    // Criterion: after checkout, the entitlement check against Supabase
    // reflects the paid tier and cloud works again
    [Fact]
    public async Task PaidSubscription_Restores_Cloud_Access()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        _supabase.EntitlementToReturn = RemoteEntitlements.Paid;

        var decision = await CreateService().EvaluateAsync();

        Assert.True(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.Paid, decision.Status);
    }

    // Criterion: entitlement is cached locally, so a later check doesn't need
    // a live Supabase call
    [Fact]
    public async Task Entitlement_Is_Cached_After_A_Successful_Sync()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        _supabase.EntitlementToReturn = RemoteEntitlements.Paid;
        var service = CreateService();

        await service.EvaluateAsync();
        Assert.Equal(RemoteEntitlements.Paid, _devices.Current.CachedEntitlement);
        Assert.Equal(Now, _devices.Current.LastSyncedAt);

        var second = await service.EvaluateAsync();

        Assert.True(second.CloudAllowed);
        Assert.Equal(1, _supabase.EntitlementCallCount);
    }

    // Criterion: the cache refreshes on a defined interval, not left
    // permanently stale
    [Fact]
    public async Task Cache_Refreshes_Once_Past_The_Refresh_Interval()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.Paid, Now.AddHours(-25));
        _supabase.EntitlementToReturn = RemoteEntitlements.Paid;

        await CreateService().EvaluateAsync();

        Assert.Equal(1, _supabase.EntitlementCallCount);
        Assert.Equal(Now, _devices.Current.LastSyncedAt);
    }

    // Criterion: cached entitlement still gates correctly when offline
    [Fact]
    public async Task Offline_Within_Grace_Uses_The_Cached_Entitlement()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.Paid, Now.AddDays(-3));
        _supabase.ExceptionToThrow = new SupabaseException("offline");

        var decision = await CreateService().EvaluateAsync();

        Assert.True(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.Paid, decision.Status);
    }

    // Criterion: Supabase unreachable and no valid cache — cloud fails closed
    // with a "can't verify" message, deliberately not fail-open
    [Fact]
    public async Task Offline_Past_Grace_Fails_Closed_With_A_CantVerify_Message()
    {
        AddCloudDictation(Now.AddDays(-60));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.Paid, Now.AddDays(-8));
        _supabase.ExceptionToThrow = new SupabaseException("offline");

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.Unverifiable, decision.Status);
        Assert.Contains("can't verify", decision.Message, StringComparison.OrdinalIgnoreCase);

        // Paying again wouldn't fix this, so no checkout route is offered.
        Assert.False(decision.OffersUpgrade);
    }

    [Fact]
    public async Task Offline_With_No_Cache_At_All_Fails_Closed()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        _supabase.ExceptionToThrow = new SupabaseException("offline");

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.Unverifiable, decision.Status);
    }

    // Criterion: cancelling via Stripe's portal reverts cloud access on the
    // next successful sync
    [Fact]
    public async Task Cancellation_Reverts_Cloud_Access_On_The_Next_Sync()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.Paid, Now.AddDays(-2));
        _supabase.EntitlementToReturn = RemoteEntitlements.None;

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.TrialExpired, decision.Status);
        Assert.True(decision.OffersUpgrade);
        Assert.Equal(RemoteEntitlements.None, _devices.Current.CachedEntitlement);
    }

    // A fresh cache saying "paid" must not be overridden by a stale trial
    // reading — and a fresh cache saying "none" must still block
    [Fact]
    public async Task FreshCache_Without_A_Subscription_Still_Blocks()
    {
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.None, Now.AddHours(-1));

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(0, _supabase.EntitlementCallCount);
    }

    // Cache duration and grace are policy, not hardcoding
    [Fact]
    public async Task Cache_Policy_Numbers_Are_Configurable()
    {
        _options.EntitlementRefreshInterval = TimeSpan.FromHours(1);
        _options.EntitlementOfflineGrace = TimeSpan.FromHours(2);
        AddCloudDictation(Now.AddDays(-31));
        await SignInAsync();
        CacheEntitlement(RemoteEntitlements.Paid, Now.AddHours(-3));
        _supabase.ExceptionToThrow = new SupabaseException("offline");

        var decision = await CreateService().EvaluateAsync();

        Assert.False(decision.CloudAllowed);
        Assert.Equal(EntitlementStatus.Unverifiable, decision.Status);
    }
}
