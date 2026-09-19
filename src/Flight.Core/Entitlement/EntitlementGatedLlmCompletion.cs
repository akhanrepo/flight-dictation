using Flight.Core.Llm;

namespace Flight.Core.Entitlement;

/// <summary>
/// Wraps the <em>cloud</em> engine only. Evaluates entitlement and throws
/// before delegating, so a blocked dictation never reaches the provider's API
/// — no request, no charge.
/// </summary>
/// <remarks>
/// Its position is what makes two of feature #5's guarantees structural rather
/// than a matter of discipline: local-mode dictation never runs an entitlement
/// check, because the router picks the local engine and this gate isn't in
/// that path; and a block always precedes the API call, because the check runs
/// before the delegate does.
/// </remarks>
public class EntitlementGatedLlmCompletion : ILlmCompletion
{
    private readonly ILlmCompletion _cloud;
    private readonly IEntitlementService _entitlement;

    public EntitlementGatedLlmCompletion(ILlmCompletion cloud, IEntitlementService entitlement)
    {
        _cloud = cloud;
        _entitlement = entitlement;
    }

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await _entitlement.EvaluateAsync(cancellationToken);
        if (!decision.CloudAllowed)
        {
            throw new EntitlementRequiredException(decision);
        }

        return await _cloud.CompleteAsync(request, cancellationToken);
    }
}
