using Flight.Core.Input;
using Flight.Core.Settings;
using Microsoft.Data.Sqlite;
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

    // Feature #8 criterion: the hotkey survives a restart
    [Fact]
    public async Task Hotkey_Survives_A_Restart()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Win", out var hotkey));
        await CreateStore().UpdateAsync(new FlightSettings { Hotkey = hotkey });

        Assert.Equal(hotkey, CreateStore().Current.Hotkey);
    }

    [Fact]
    public void Hotkey_Defaults_To_Right_Alt()
    {
        Assert.Equal(Hotkey.Default, CreateStore().Current.Hotkey);
    }

    // Feature #8 criterion: a database created before the hotkey setting
    // existed is migrated in place and keeps Right Alt
    [Fact]
    public async Task Database_From_Before_Feature8_Is_Migrated_And_Keeps_Right_Alt()
    {
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL,
                    llm_mode TEXT NOT NULL DEFAULT 'local'
                );
                INSERT INTO settings (id, source_language, target_language, llm_mode) VALUES (1, 'ur', 'en', 'cloud');
                """;
            command.ExecuteNonQuery();
        }

        var store = CreateStore();

        Assert.Equal("ur", store.Current.SourceLanguage);
        Assert.Equal(Hotkey.Default, store.Current.Hotkey);

        // And the new column is writable.
        Assert.True(Hotkey.TryParse("F8", out var f8));
        await store.UpdateAsync(store.Current with { Hotkey = f8 });
        Assert.Equal(f8, CreateStore().Current.Hotkey);
    }

    [Fact]
    public async Task Unusable_Hotkey_Is_Rejected()
    {
        Assert.True(Hotkey.TryParse("A", out var bareLetter));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateStore().UpdateAsync(new FlightSettings { Hotkey = bareLetter }));
    }

    [Fact]
    public async Task Update_Raises_Changed()
    {
        var store = CreateStore();
        FlightSettings? seen = null;
        store.Changed += s => seen = s;

        var updated = await store.UpdateAsync(new FlightSettings { TargetLanguage = "fr" });

        Assert.Same(updated, seen);
    }
}
