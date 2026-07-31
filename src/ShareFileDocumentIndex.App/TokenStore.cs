using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShareFileDocumentIndex.App;

public sealed class StoredToken
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string ApiCp { get; set; } = "sf-api.com";
    public string AccountSubdomain { get; set; } = "";
}

// Persists the signed-in session to disk (DPAPI-encrypted, tied to the current Windows
// user) so coworkers only go through the browser sign-in once instead of every launch.
public static class TokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShareFileDocumentIndex",
        "session.dat");

    public static void Save(StoredToken token)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(token);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static StoredToken? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<StoredToken>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
