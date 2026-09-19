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

    public Task<FlightSettings> UpdateAsync(FlightSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();

        lock (_writeLock)
        {
            Save(settings);
            Volatile.Write(ref _current, settings);
        }

        return Task.FromResult(settings);
    }

    private FlightSettings Load()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_language, target_language
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
        };
    }

    private void Save(FlightSettings settings)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (id, source_language, target_language)
            VALUES (1, $source, $target)
            ON CONFLICT (id) DO UPDATE SET
                source_language = excluded.source_language,
                target_language = excluded.target_language;
            """;
        command.Parameters.AddWithValue("$source", settings.SourceLanguage);
        command.Parameters.AddWithValue("$target", settings.TargetLanguage);
        command.ExecuteNonQuery();
    }
}
