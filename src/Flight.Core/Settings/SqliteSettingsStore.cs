using Flight.Core.Input;
using Flight.Core.Llm;
using Flight.Core.Storage;

namespace Flight.Core.Settings;

/// <summary>
/// Single-row settings table, cached in memory. Reads never hit SQLite: the
/// dictation loop asks for <see cref="Current"/> on every trigger and must not
/// pay disk latency for it.
/// </summary>
public class SqliteSettingsStore : ISettingsStore
{
    private readonly FlightDatabase _database;
    private readonly object _writeLock = new();
    private FlightSettings _current;

    public SqliteSettingsStore(FlightDatabase database)
    {
        _database = database;
        _current = Load();
    }

    public FlightSettings Current => Volatile.Read(ref _current);

    public event Action<FlightSettings>? Changed;

    public Task<FlightSettings> UpdateAsync(FlightSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();

        lock (_writeLock)
        {
            Save(settings);
            Volatile.Write(ref _current, settings);
        }

        Changed?.Invoke(settings);
        return Task.FromResult(settings);
    }

    private FlightSettings Load()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_language, target_language, llm_mode, hotkey
            FROM settings
            WHERE id = 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            // First run: defaults, not an error.
            return new FlightSettings();
        }

        return new FlightSettings
        {
            SourceLanguage = reader.GetString(0),
            TargetLanguage = reader.GetString(1),
            LlmMode = LlmModes.Parse(reader.GetString(2)),
            // Rows from before feature #8 have no hotkey: Right Alt, as before.
            Hotkey = !reader.IsDBNull(3) && Hotkey.TryParse(reader.GetString(3), out var hotkey) && hotkey.Validate() is null
                ? hotkey
                : Hotkey.Default,
        };
    }

    private void Save(FlightSettings settings)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (id, source_language, target_language, llm_mode, hotkey)
            VALUES (1, $source, $target, $llmMode, $hotkey)
            ON CONFLICT (id) DO UPDATE SET
                source_language = excluded.source_language,
                target_language = excluded.target_language,
                llm_mode = excluded.llm_mode,
                hotkey = excluded.hotkey;
            """;
        command.Parameters.AddWithValue("$source", settings.SourceLanguage);
        command.Parameters.AddWithValue("$target", settings.TargetLanguage);
        command.Parameters.AddWithValue("$llmMode", settings.LlmMode.ToStorageValue());
        command.Parameters.AddWithValue("$hotkey", settings.Hotkey.ToString());
        command.ExecuteNonQuery();
    }
}
