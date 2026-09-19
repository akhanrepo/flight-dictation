namespace Flight.Core.Vocabulary;

/// <summary>
/// float32 little-endian packing for embeddings stored as SQLite BLOBs — the
/// same layout sqlite-vec's <c>vec0</c> tables expect, so the stored bytes
/// survive a later move to the extension.
/// </summary>
public static class EmbeddingBlob
{
    public static byte[] ToBlob(float[] embedding)
    {
        var blob = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, blob, 0, blob.Length);
        return blob;
    }

    public static float[] FromBlob(byte[] blob)
    {
        var embedding = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, embedding, 0, embedding.Length * sizeof(float));
        return embedding;
    }
}
