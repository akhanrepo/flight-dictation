namespace Flight.Core.Llm;

/// <summary>
/// A cloud cleanup call failed. Always surfaces to the user as an explicit
/// error — there is deliberately no silent fallback to local inference or to a
/// stale result, so the user always knows which engine produced their text.
/// </summary>
public class CloudLlmException : Exception
{
    public CloudLlmException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
