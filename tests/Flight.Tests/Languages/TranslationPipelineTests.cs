using Flight.Core;
using Flight.Core.Dictation;
using Flight.Core.Languages;
using Flight.Core.Llm;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flight.Tests.Languages;

/// <summary>
/// Feature #3 acceptance criteria: source/target language handling through the
/// existing dictation pipeline, in both directions.
/// </summary>
public class TranslationPipelineTests
{
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeAudioCapturer _capturer = new();
    private readonly FakeSpeechToText _stt = new();
    private readonly FakeTextCleaner _cleaner = new();
    private readonly FakeClipboardWriter _clipboard = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly FakeDictationUi _ui = new();
    private readonly FakeVocabularyLearningQueue _learning = new();
    private readonly FakeSettingsStore _settings = new();

    private DictationOrchestrator CreateOrchestrator()
    {
        var orchestrator = new DictationOrchestrator(
            _hotkey, _capturer, _stt, _cleaner, _clipboard, _sessions, _ui, _learning,
            _settings, new FlightOptions(), NullLogger<DictationOrchestrator>.Instance);
        orchestrator.Start();
        return orchestrator;
    }

    private async Task<DictationOrchestrator> DictateAsync()
    {
        var orchestrator = CreateOrchestrator();
        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        return orchestrator;
    }

    private async Task SetLanguagesAsync(string source, string target) =>
        await _settings.UpdateAsync(new FlightSettings { SourceLanguage = source, TargetLanguage = target });

    // Criterion: the settings surface offers exactly the six named languages
    [Fact]
    public void SupportedLanguages_Are_The_Six_From_The_Brief()
    {
        Assert.Equal(
            ["en", "ur", "ar", "es", "fr", "zh"],
            SupportedLanguages.All.Select(language => language.Code));
        Assert.Equal(
            ["English", "Urdu", "Arabic", "Spanish", "French", "Mandarin"],
            SupportedLanguages.All.Select(language => language.EnglishName));
    }

    // Criterion: target language is configurable independently, defaulting to English
    [Fact]
    public void Default_Language_Pair_Is_English_To_English()
    {
        var settings = new FlightSettings();

        Assert.Equal("en", settings.SourceLanguage);
        Assert.Equal("en", settings.TargetLanguage);
        Assert.False(settings.IsTranslating);
    }

    // Criterion: STT runs in the selected source language, not auto-detected
    [Fact]
    public async Task Stt_Runs_In_The_Selected_Source_Language()
    {
        await SetLanguagesAsync("ur", "en");

        await DictateAsync();

        Assert.Equal("ur", _stt.ReceivedLanguage);
    }

    // Criterion: source == target behaves like feature #1 — cleanup only, no
    // translation, and not an error state
    [Fact]
    public async Task SameSourceAndTarget_Is_CleanupOnly_And_Not_An_Error()
    {
        await SetLanguagesAsync("es", "es");

        var orchestrator = await DictateAsync();

        Assert.Equal(DictationState.Idle, orchestrator.State);
        Assert.Single(_clipboard.Writes);
        Assert.False(_cleaner.ReceivedRequest!.RequiresTranslation);
    }

    // Criterion: differing source and target sends both languages to the
    // cleanup step, which translates as well as cleans
    [Fact]
    public async Task DifferentSourceAndTarget_Requests_Translation()
    {
        await SetLanguagesAsync("ar", "en");

        await DictateAsync();

        Assert.Equal("ar", _cleaner.ReceivedRequest!.SourceLanguage);
        Assert.Equal("en", _cleaner.ReceivedRequest.TargetLanguage);
        Assert.True(_cleaner.ReceivedRequest.RequiresTranslation);
    }

    // Criterion: reverse direction works through the same setting
    [Fact]
    public async Task ReverseDirection_English_To_Arabic_Works_The_Same_Way()
    {
        await SetLanguagesAsync("en", "ar");
        _cleaner.CleanedToReturn = "مرحبا بالعالم";

        await DictateAsync();

        Assert.Equal("en", _stt.ReceivedLanguage);
        Assert.Equal("ar", _cleaner.ReceivedRequest!.TargetLanguage);
        Assert.Equal("مرحبا بالعالم", _clipboard.LastText);
    }

    // Criterion: the translated cleaned text is what gets copied, not the
    // raw source-language transcript
    [Fact]
    public async Task Copies_The_Translated_Text_Not_The_Raw_Transcript()
    {
        await SetLanguagesAsync("es", "en");
        _stt.TranscriptToReturn = "eh hola eh que tal el proyecto";
        _cleaner.CleanedToReturn = "How is the project going?";

        var orchestrator = await DictateAsync();

        Assert.Equal(["How is the project going?"], _clipboard.Writes);
        Assert.Equal(DictationState.Idle, orchestrator.State);
    }

    [Fact]
    public async Task Failure_Leaves_The_Clipboard_Untouched_When_Translating()
    {
        await SetLanguagesAsync("fr", "en");
        _cleaner.ExceptionToThrow = new InvalidOperationException("translation failed");

        var orchestrator = await DictateAsync();

        Assert.Empty(_clipboard.Writes);
        Assert.Equal(DictationState.Error, orchestrator.State);
    }

    // Criterion: session rows carry source_language and target_language for
    // all six languages, never left null
    [Theory]
    [InlineData("en", "en")]
    [InlineData("ur", "en")]
    [InlineData("ar", "en")]
    [InlineData("es", "en")]
    [InlineData("fr", "en")]
    [InlineData("zh", "en")]
    [InlineData("en", "ur")]
    [InlineData("en", "zh")]
    public async Task Session_Records_The_Language_Pair(string source, string target)
    {
        await SetLanguagesAsync(source, target);

        await DictateAsync();

        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal(source, session.SourceLanguage);
        Assert.Equal(target, session.TargetLanguage);
    }

    // Criterion: switching language mid-session, without restarting, takes
    // effect on the next dictation
    [Fact]
    public async Task LanguageChange_MidSession_Applies_To_The_Next_Dictation()
    {
        await SetLanguagesAsync("en", "en");
        var orchestrator = CreateOrchestrator();

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;
        Assert.Equal("en", _stt.ReceivedLanguage);

        // No restart, no new orchestrator.
        await SetLanguagesAsync("zh", "en");

        _hotkey.Press();
        _hotkey.Release();
        await orchestrator.ProcessingTask!;

        Assert.Equal("zh", _stt.ReceivedLanguage);
        Assert.Equal("zh", _sessions.Sessions[1].SourceLanguage);
    }

    // A language change must not leak into a dictation that is already in
    // flight — settings are snapshotted per dictation
    [Fact]
    public async Task LanguageChange_Does_Not_Affect_An_InFlight_Dictation()
    {
        await SetLanguagesAsync("fr", "en");
        var orchestrator = CreateOrchestrator();
        _stt.Delay = TimeSpan.FromMilliseconds(50);

        _hotkey.Press();
        _hotkey.Release();
        await SetLanguagesAsync("zh", "en");
        await orchestrator.ProcessingTask!;

        Assert.Equal("fr", _sessions.Sessions[0].SourceLanguage);
    }

    // An unsupported language never reaches the pipeline
    [Fact]
    public async Task UnsupportedLanguage_Is_Rejected_By_Settings()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _settings.UpdateAsync(new FlightSettings { SourceLanguage = "de", TargetLanguage = "en" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _settings.UpdateAsync(new FlightSettings { SourceLanguage = "en", TargetLanguage = "jp" }));
    }
}
