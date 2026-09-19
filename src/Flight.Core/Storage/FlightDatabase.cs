using Microsoft.Data.Sqlite;

namespace Flight.Core.Storage;

/// <summary>
/// Owns the SQLite connection string and schema creation. Schema mirrors the
/// PRD data model; future tables (vocabulary_entries, etc.) are added here by
/// the features that introduce them.
/// </summary>
public class FlightDatabase
{
    private readonly string _connectionString;
    private bool _initialized;
    private readonly object _initLock = new();

    public FlightDatabase(FlightOptions options)
        : this($"Data Source={options.DatabasePath}", options.DataDirectory)
    {
    }

    public FlightDatabase(string connectionString, string? dataDirectory = null)
    {
        _connectionString = connectionString;
        if (dataDirectory is not null)
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    public SqliteConnection OpenConnection()
    {
        EnsureSchema();
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS dictation_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    raw_transcript TEXT NOT NULL,
                    cleaned_output TEXT NOT NULL,
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL,
                    llm_mode TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS vocabulary_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    term TEXT NOT NULL,
                    embedding BLOB NOT NULL,
                    frequency INTEGER NOT NULL,
                    last_used_at TEXT NOT NULL,
                    source_session_id INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );

                -- Exact-string dedup (feature #2 scope): the same term never
                -- gets a second row, it increments the first one's frequency.
                CREATE UNIQUE INDEX IF NOT EXISTS ix_vocabulary_entries_term
                    ON vocabulary_entries (term COLLATE NOCASE);

                -- Per-device settings; a single row, id pinned to 1.
                CREATE TABLE IF NOT EXISTS settings (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL,
                    llm_mode TEXT NOT NULL DEFAULT 'local'
                );

                -- This install and its cached entitlement (PRD: Device).
                -- A single row, row_id pinned to 1; the device id itself is a
                -- stable guid generated on first run.
                CREATE TABLE IF NOT EXISTS devices (
                    row_id INTEGER PRIMARY KEY CHECK (row_id = 1),
                    id TEXT NOT NULL,
                    account_id TEXT NULL,
                    cached_entitlement TEXT NULL,
                    last_synced_at TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
