using System.Globalization;
using System.Text;

namespace Flight.Core.Vocabulary;

/// <summary>
/// Dependency-free embedding: character trigrams hashed into a fixed-width
/// bag, L2-normalized. Lexical rather than semantic, but deterministic,
/// instant, and model-free — so the vocabulary pipeline works before the GGUF
/// has downloaded and tests never touch a model file.
/// </summary>
/// <remarks>
/// Trigrams (not whole words) mean "Contoso" still scores against "Contoso's"
/// and against a near-miss transcription, which is the recall that matters for
/// client names and product jargon.
/// </remarks>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator
{
    private const int TrigramSize = 3;

    public HashingEmbeddingGenerator(int dimensions = 256)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Embedding width must be positive.");
        }

        Dimensions = dimensions;
    }

    public int Dimensions { get; }

    public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(vector);
        }

        // Pad so short terms still produce trigrams, and so word boundaries
        // themselves become features.
        var normalized = " " + text.ToLower(CultureInfo.InvariantCulture).Trim() + " ";
        for (var i = 0; i + TrigramSize <= normalized.Length; i++)
        {
            var bucket = Bucket(normalized.AsSpan(i, TrigramSize));
            vector[bucket] += 1;
        }

        return Task.FromResult(VectorMath.Normalize(vector));
    }

    private int Bucket(ReadOnlySpan<char> trigram)
    {
        // FNV-1a: cheap, well-distributed, and stable across processes, which
        // matters because these vectors are persisted.
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in trigram)
        {
            hash ^= c;
            hash *= prime;
        }

        return (int)(hash % (uint)Dimensions);
    }
}
