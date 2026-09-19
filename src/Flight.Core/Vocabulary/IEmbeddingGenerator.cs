namespace Flight.Core.Vocabulary;

/// <summary>
/// Turns text into a vector for similarity retrieval. Implementations must
/// return vectors of a stable length for a given instance, and must be usable
/// offline — vocabulary retrieval sits in the dictation hot path.
/// </summary>
public interface IEmbeddingGenerator
{
    int Dimensions { get; }

    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default);
}
