namespace Flight.Core.Entitlement;

/// <summary>
/// The answer to "may this device make a cloud call right now?", plus the
/// user-facing reason. Local-mode dictation never asks this question.
/// </summary>
/// <param name="CloudAllowed">False means the cloud call must not be made.</param>
/// <param name="Message">Shown to the user when blocked. Never empty when blocked.</param>
/// <param name="TrialEndsAt">End of the 30-day window, when a trial is what's in play.</param>
/// <param name="OffersUpgrade">
/// True when paying resolves the block — drives whether the UI shows a
/// checkout route. False for "we couldn't verify", where paying again wouldn't help.
/// </param>
public record EntitlementDecision(
    bool CloudAllowed,
    EntitlementStatus Status,
    string Message = "",
    DateTimeOffset? TrialEndsAt = null,
    bool OffersUpgrade = false);
