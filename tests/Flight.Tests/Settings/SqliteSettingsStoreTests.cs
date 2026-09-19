using Flight.Core.Settings;
using Flight.Core.Storage;
using Xunit;

namespace Flight.Tests.Settings;

/// <summary>
/// Feature #3: the language pair is a persisted user setting, not per-run state.
/// </summary>
public class SqliteSettingsStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"flight-settings-{Guid.NewGuid():N}.db");

    private SqliteSettingsStore CreateStore() =>
        new(new FlightDatabase($"Data Source={_databasePath}"));

    // First run has no row: defaults, not an error
    [Fact]
    public void FirstRun_Defaults_To_English_To_English()
    {
        var store = CreateStore();

        Assert.Equal("en", store.Current.SourceLanguage);
        Assert.Equal("en", store.Current.TargetLanguage);
    }

    // Criterion: the selected pair survives a restart
    [Fact]
    public async Task SelectedLanguages_Survive_A_Restart()
    {
        await CreateStore().UpdateAsync(new FlightSettings { SourceLanguage = "ur", TargetLanguage = "en" });

        // A second store instance stands in for a fresh app launch.
        var reopened = CreateStore();

        Assert.Equal("ur", reopened.Current.SourceLanguage);
        Assert.Equal("en", reopened.Current.TargetLanguage);
    }

    // Criterion: a change is visible immediately, without a restart
    [Fact]
    public async Task Update_Is_Visible_Immediately_On_The_Same_Instance()
    {
        var store = CreateStore();

        await store.UpdateAsync(new FlightSettings { SourceLanguage = "en", TargetLanguage = "fr" });

        Assert.Equal("fr", store.Current.TargetLanguage);
        Assert.True(store.Current.IsTranslating);
    }

    // Updating twice must not create a second settings row
    [Fact]
    public async Task RepeatedUpdates_Keep_A_Single_Row()
    {
        var store = CreateStore();

        await store.UpdateAsync(new FlightSettings { SourceLanguage = "es", TargetLanguage = "en" });
        await store.UpdateAsync(new FlightSettings { SourceLanguage = "zh", TargetLanguage = "en" });

        Assert.Equal("zh", CreateStore().Current.SourceLanguage);
    }

    [Fact]
    public async Task UnsupportedLanguage_Is_Not_Persisted()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpdateAsync(new FlightSettings { SourceLanguage = "de", TargetLanguage = "en" }));

        Assert.Equal("en", store.Current.SourceLanguage);
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
