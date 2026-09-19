namespace Flight.Core.Vocabulary;

public static class VectorMath
{
    /// <summary>
    /// Cosine similarity in [-1, 1]. Returns 0 for a zero-magnitude or
    /// mismatched-length pair rather than throwing — a malformed stored
    /// embedding should drop out of the ranking, not break the dictation loop.
    /// </summary>
    public static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0;
        }

        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * (double)right[i];
            leftMagnitude += left[i] * (double)left[i];
            rightMagnitude += right[i] * (double)right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    public static float[] Normalize(float[] vector)
    {
        double magnitude = 0;
        foreach (var value in vector)
        {
            magnitude += value * (double)value;
        }

        if (magnitude == 0)
        {
            return vector;
        }

        var scale = 1 / Math.Sqrt(magnitude);
        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = (float)(vector[i] * scale);
        }

        return normalized;
    }
}
