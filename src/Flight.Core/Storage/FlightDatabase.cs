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
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
