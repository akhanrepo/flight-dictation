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
                    llm_mode TEXT NOT NULL DEFAULT 'local',
                    hotkey TEXT NULL
                );
                """;
            command.ExecuteNonQuery();

            // Columns added after a table first shipped. CREATE TABLE IF NOT
            // EXISTS leaves an existing table alone, so add them in place.
            AddColumnIfMissing(connection, "settings", "hotkey", "TEXT NULL");

            _initialized = true;
        }
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
        check.Parameters.AddWithValue("$column", column);
        // COUNT(*) always yields exactly one row, so the scalar is never null.
        if ((long)check.ExecuteScalar()! > 0)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
