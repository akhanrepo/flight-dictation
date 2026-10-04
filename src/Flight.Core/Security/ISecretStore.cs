namespace Flight.Core.Security;

/// <summary>
/// Local storage for credentials — the cloud provider's API key. Implementations
/// must encrypt at rest (the shell uses Windows DPAPI, per-user); a plaintext
/// config file is not an acceptable implementation.
/// </summary>
public interface ISecretStore
{
    /// <returns>The stored secret, or null when none has been set.</returns>
    string? Get(string name);

    void Set(string name, string value);

    void Remove(string name);
}

public static class SecretNames
{
    public const string CloudLlmApiKey = "cloud-llm-api-key";
    public const string AccountAccessToken = "supabase-access-token";
    public const string AccountRefreshToken = "supabase-refresh-token";
    public const string AccountEmail = "supabase-account-email";
}
