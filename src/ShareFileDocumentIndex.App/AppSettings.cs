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
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettingsRoot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return new AppSettings { ShareFile = settings?.ShareFile ?? new ShareFileSettings() };
    }

    private sealed class AppSettingsRoot
    {
        public ShareFileSettings ShareFile { get; set; } = new();
    }
}
