using Flight.Core.Storage;
using Xunit;

namespace Flight.Tests.Storage;

public sealed class SqliteDictationSessionRepositoryTests : IDisposable
{
    private readonly string _dbDirectory;
    private readonly SqliteDictationSessionRepository _repository;

    public SqliteDictationSessionRepositoryTests()
    {
        _dbDirectory = Path.Combine(Path.GetTempPath(), "flight-tests-" + Guid.NewGuid().ToString("N"));
        var database = new FlightDatabase($"Data Source={Path.Combine(_dbDirectory, "test.db")}", _dbDirectory);
        _repository = new SqliteDictationSessionRepository(database);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dbDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Add_Persists_Session_With_All_Fields_RoundTrip()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var added = await _repository.AddAsync(new DictationSession
        {
            RawTranscript = "um so the client uh wants the audit by friday",
            CleanedOutput = "The client wants the audit by Friday.",
            LlmMode = "local",
            CreatedAt = createdAt,
        });

        Assert.True(added.Id > 0);

        var all = await _repository.GetAllAsync();
        var stored = Assert.Single(all);
        Assert.Equal("um so the client uh wants the audit by friday", stored.RawTranscript);
        Assert.Equal("The client wants the audit by Friday.", stored.CleanedOutput);
        Assert.Equal("local", stored.LlmMode);
        Assert.Equal("en", stored.SourceLanguage);
        Assert.Equal("en", stored.TargetLanguage);
        Assert.Equal(createdAt, stored.CreatedAt);
    }

    [Fact]
    public async Task GetAll_Returns_Sessions_In_Insertion_Order()
    {
        await _repository.AddAsync(new DictationSession { RawTranscript = "first", CleanedOutput = "First." });
        await _repository.AddAsync(new DictationSession { RawTranscript = "second", CleanedOutput = "Second." });

        var all = await _repository.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("First.", all[0].CleanedOutput);
        Assert.Equal("Second.", all[1].CleanedOutput);
    }

    [Fact]
    public async Task GetAll_On_Empty_Database_Returns_Empty()
    {
        Assert.Empty(await _repository.GetAllAsync());
    }
}
