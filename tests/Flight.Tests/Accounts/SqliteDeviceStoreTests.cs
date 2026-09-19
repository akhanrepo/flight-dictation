using Flight.Core.Accounts;
using Flight.Core.Entitlement;
using Flight.Core.Storage;
using Xunit;

namespace Flight.Tests.Accounts;

/// <summary>
/// Feature #5: the Device row and its entitlement cache, against real SQLite.
/// </summary>
public class SqliteDeviceStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"flight-device-{Guid.NewGuid():N}.db");

    private SqliteDeviceStore CreateStore() => new(new FlightDatabase($"Data Source={_databasePath}"));

    // Criterion: the install has a Device row (id, account_id, entitlement
    // cache, last_synced_at)
    [Fact]
    public void FirstRun_Creates_A_Device_With_No_Account_Or_Cache()
    {
        var device = CreateStore().Current;

        Assert.NotEmpty(device.Id);
        Assert.Null(device.AccountId);
        Assert.Null(device.CachedEntitlement);
        Assert.Null(device.LastSyncedAt);
    }

    // The device id must be stable, or the entitlement cache can't be trusted
    // across restarts
    [Fact]
    public void DeviceId_Is_Stable_Across_Restarts()
    {
        var first = CreateStore().Current.Id;

        Assert.Equal(first, CreateStore().Current.Id);
    }

    // Criterion: entitlement status is cached locally so offline gating works
    // without a live Supabase check on every launch
    [Fact]
    public async Task CachedEntitlement_Survives_A_Restart()
    {
        var syncedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await CreateStore().UpdateAsync(CreateStore().Current with
        {
            AccountId = "account-1",
            CachedEntitlement = RemoteEntitlements.Paid,
            LastSyncedAt = syncedAt,
        });

        var reopened = CreateStore().Current;

        Assert.Equal("account-1", reopened.AccountId);
        Assert.Equal(RemoteEntitlements.Paid, reopened.CachedEntitlement);
        Assert.Equal(syncedAt.ToUnixTimeSeconds(), reopened.LastSyncedAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task ClearedCache_Survives_A_Restart_Too()
    {
        var store = CreateStore();
        await store.UpdateAsync(store.Current with
        {
            AccountId = "account-1",
            CachedEntitlement = RemoteEntitlements.Paid,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await store.UpdateAsync(store.Current with
        {
            AccountId = null,
            CachedEntitlement = null,
            LastSyncedAt = null,
        });

        var reopened = CreateStore().Current;

        Assert.Null(reopened.AccountId);
        Assert.Null(reopened.CachedEntitlement);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
        GC.SuppressFinalize(this);
    }
}
