using Flight.Core.Settings;

namespace Flight.Core.Llm;

/// <summary>
/// Routes each completion to the local or cloud engine. Everything downstream
/// of it — cleanup, translation, vocabulary extraction — is written against
/// <see cref="ILlmCompletion"/> and never learns which engine ran.
/// </summary>
/// <remarks>
/// A request carrying an explicit <see cref="LlmRequest.Mode"/> is routed by
/// that; the dictation loop sets it from its per-dictation settings snapshot,
/// so toggling mid-dictation cannot switch engines halfway through the one
/// already running. Requests without one (background vocabulary extraction)
/// follow the current setting. There is no fallback path: a cloud failure
/// surfaces as a cloud failure.
/// </remarks>
public class LlmModeRouter : ILlmCompletion
{
    private readonly LocalLlmEngine _local;
    private readonly CloudLlmEngine _cloud;
    private readonly ISettingsStore _settings;

    public LlmModeRouter(LocalLlmEngine local, CloudLlmEngine cloud, ISettingsStore settings)
    {
        _local = local;
        _cloud = cloud;
        _settings = settings;
    }

    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var mode = request.Mode ?? _settings.Current.LlmMode;
        var engine = mode == LlmMode.Cloud ? _cloud.Inner : _local.Inner;
        return engine.CompleteAsync(request, cancellationToken);
    }
}

/// <summary>
/// Names the local engine for DI. Both engines and the router itself implement
/// <see cref="ILlmCompletion"/>, so the container needs distinct types to tell
/// them apart.
/// </summary>
public sealed class LocalLlmEngine
{
    public LocalLlmEngine(ILlmCompletion inner) => Inner = inner;

    public ILlmCompletion Inner { get; }
}

/// <summary>Names the cloud engine for DI. See <see cref="LocalLlmEngine"/>.</summary>
public sealed class CloudLlmEngine
{
    public CloudLlmEngine(ILlmCompletion inner) => Inner = inner;

    public ILlmCompletion Inner { get; }
}
