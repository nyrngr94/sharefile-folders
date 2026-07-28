using System.IO;
using System.Text.Json;

namespace ShareFileDocumentIndex.App;

public sealed class ShareFileSettings
{
    public string Subdomain { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RootFolderPath { get; set; } = "";
}

public sealed class AppSettings
{
    public ShareFileSettings ShareFile { get; set; } = new();

    public static AppSettings Load()
    {
        var result = new AppSettings();

        LoadInto(result, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        // appsettings.local.json is git-ignored and holds the real Client ID/Secret;
        // it overrides the placeholder values checked into appsettings.json.
        LoadInto(result, Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"));

        return result;
    }

    private static void LoadInto(AppSettings target, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettingsRoot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (settings?.ShareFile is null)
        {
            return;
        }

        var s = settings.ShareFile;
        if (!string.IsNullOrWhiteSpace(s.Subdomain)) target.ShareFile.Subdomain = s.Subdomain;
        if (!string.IsNullOrWhiteSpace(s.ClientId)) target.ShareFile.ClientId = s.ClientId;
        if (!string.IsNullOrWhiteSpace(s.ClientSecret)) target.ShareFile.ClientSecret = s.ClientSecret;
        if (!string.IsNullOrWhiteSpace(s.RootFolderPath)) target.ShareFile.RootFolderPath = s.RootFolderPath;
    }

    private sealed class AppSettingsRoot
    {
        public ShareFileSettings ShareFile { get; set; } = new();
    }
}
