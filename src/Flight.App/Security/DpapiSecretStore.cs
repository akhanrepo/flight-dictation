using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flight.Core;
using Flight.Core.Security;

namespace Flight.App.Security;

/// <summary>
/// Secrets encrypted with Windows DPAPI under the current user account, so the
/// file is unreadable by other users on the machine and never contains a
/// plaintext key. Stored beside the database in the app's data directory.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly object _fileLock = new();

    public DpapiSecretStore(FlightOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _path = Path.Combine(options.DataDirectory, "secrets.dat");
    }

    public string? Get(string name)
    {
        lock (_fileLock)
        {
            return Read().TryGetValue(name, out var value) ? value : null;
        }
    }

    public void Set(string name, string value)
    {
        lock (_fileLock)
        {
            var secrets = Read();
            secrets[name] = value;
            Write(secrets);
        }
    }

    public void Remove(string name)
    {
        lock (_fileLock)
        {
            var secrets = Read();
            if (secrets.Remove(name))
            {
                Write(secrets);
            }
        }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plaintext)) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Unreadable (different user, or a corrupt file): treat as "no
            // secrets saved". Cloud mode then reports a missing key, which is
            // the accurate, actionable message.
            return [];
        }
    }

    private void Write(Dictionary<string, string> secrets)
    {
        var ciphertext = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets)),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, ciphertext);
    }
}
