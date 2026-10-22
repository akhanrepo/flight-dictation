using Flight.Core.Models;

namespace Flight.Core.Dictation;

/// <summary>
/// Visual surface for the dictation loop: the small indicator pill. The
/// orchestrator drives it; it never takes keyboard focus, so the user's app
/// stays active and can be pasted into the moment the text is copied.
/// </summary>
public interface IDictationUi
{
    /// <summary>Visible cue that capture is active.</summary>
    void ShowCapturing();

    /// <summary>Microphone level while capturing, 0–1. Called many times a second.</summary>
    void ShowLevel(float level);

    void ShowProcessing();

    /// <summary>The result is on the clipboard.</summary>
    void ShowCopied();

    /// <summary>Silence or non-speech: nothing was copied.</summary>
    void ShowNothingHeard();

    /// <summary>Explicit, user-visible error state — never a silent failure.</summary>
    void ShowError(string message);

    /// <summary>First-run model download/init progress — never a silent hang.</summary>
    void ShowModelDownloadProgress(ModelDownloadProgress progress);

    void Hide();
}
