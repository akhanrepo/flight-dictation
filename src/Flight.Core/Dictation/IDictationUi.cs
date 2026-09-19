using Flight.Core.Entitlement;
using Flight.Core.Models;

namespace Flight.Core.Dictation;

/// <summary>
/// Visual surface for the dictation loop, implemented by the WinUI shell.
/// The orchestrator drives it; Confirm/Reject flow back as orchestrator calls.
/// </summary>
public interface IDictationUi
{
    /// <summary>Visible cue that capture is active.</summary>
    void ShowCapturing();

    void ShowProcessing();

    /// <summary>Preview of cleaned text; user confirms or dismisses from here.</summary>
    void ShowPreview(string cleanedText);

    /// <summary>Explicit, user-visible error state — never a silent failure.</summary>
    void ShowError(string message);

    /// <summary>
    /// Cloud mode was blocked by entitlement. Distinct from
    /// <see cref="ShowError"/> because the user needs a route forward
    /// (subscribe, or switch to local), not just a message.
    /// </summary>
    void ShowUpgradeRequired(EntitlementDecision decision);

    /// <summary>First-run model download/init progress — never a silent hang.</summary>
    void ShowModelDownloadProgress(ModelDownloadProgress progress);

    void Hide();
}
