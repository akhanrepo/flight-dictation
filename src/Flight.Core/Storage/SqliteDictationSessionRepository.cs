using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flight.Core.Storage;

public class SqliteDictationSessionRepository : IDictationSessionRepository
{
    private readonly FlightDatabase _database;

    public SqliteDictationSessionRepository(FlightDatabase database)
    {
        _database = database;
    }

    public Task<DictationSession> AddAsync(DictationSession session, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dictation_sessions
                (raw_transcript, cleaned_output, source_language, target_language, llm_mode, created_at)
            VALUES ($raw, $cleaned, $source, $target, $mode, $createdAt)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$raw", session.RawTranscript);
        command.Parameters.AddWithValue("$cleaned", session.CleanedOutput);
        command.Parameters.AddWithValue("$source", session.SourceLanguage);
        command.Parameters.AddWithValue("$target", session.TargetLanguage);
        command.Parameters.AddWithValue("$mode", session.LlmMode);
        command.Parameters.AddWithValue("$createdAt", session.CreatedAt.ToString("O", CultureInfo.InvariantCulture));

        var id = (long)command.ExecuteScalar()!;
        return Task.FromResult(session with { Id = id });
    }

    public Task<DateTimeOffset?> GetEarliestCloudDictationAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();

        // MIN over the ISO-8601 round-trip strings is a correct chronological
        // MIN: the format is fixed-width and lexicographically ordered.
        command.CommandText = """
            SELECT MIN(created_at)
            FROM dictation_sessions
            WHERE llm_mode = 'cloud';
            """;

        var result = command.ExecuteScalar();
        return Task.FromResult<DateTimeOffset?>(
            result is string value && value.Length > 0
                ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
                : null);
    }

    public Task<IReadOnlyList<DictationSession>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, raw_transcript, cleaned_output, source_language, target_language, llm_mode, created_at
            FROM dictation_sessions
            ORDER BY id;
            """;

        var sessions = new List<DictationSession>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new DictationSession
            {
                Id = reader.GetInt64(0),
                RawTranscript = reader.GetString(1),
                CleanedOutput = reader.GetString(2),
                SourceLanguage = reader.GetString(3),
                TargetLanguage = reader.GetString(4),
                LlmMode = reader.GetString(5),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            });
        }

        return Task.FromResult<IReadOnlyList<DictationSession>>(sessions);
    }
}
