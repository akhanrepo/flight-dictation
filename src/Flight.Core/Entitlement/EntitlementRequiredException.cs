namespace Flight.Core.Entitlement;

/// <summary>
/// Thrown instead of making a cloud call the device isn't entitled to. Carries
/// the decision so the UI can show the right prompt — an upgrade route when
/// paying would resolve it, a plain explanation when it wouldn't.
/// </summary>
public class EntitlementRequiredException : Exception
{
    public EntitlementRequiredException(EntitlementDecision decision)
        : base(decision.Message)
    {
        Decision = decision;
    }

    public EntitlementDecision Decision { get; }
}
