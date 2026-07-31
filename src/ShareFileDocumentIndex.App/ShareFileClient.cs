using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareFileDocumentIndex.App;

public sealed class ShareFileItemInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? CreationDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? CreatorName { get; set; }
    public string? CreatorCompany { get; set; }
    public string? OwnerName { get; set; }
}

public sealed class ShareFileAuthException : Exception
{
    public ShareFileAuthException(string message) : base(message) { }
}

public sealed class ShareFileClient : IDisposable
{
    // Must exactly match the Redirect URI configured on the ShareFile API app.
    // ShareFile requires a real https:// URL here -- it rejects localhost/127.0.0.1.
    // httpbin.org/anything just echoes the request back as JSON, so the "code" query
    // param ShareFile appends shows up directly in the page for the user to copy.
    public const string RedirectUri = "https://httpbin.org/anything";

    private readonly HttpClient _http = new();
    private string _baseUrl = "";

    public bool IsSignedIn { get; private set; }

    public string BuildAuthorizeUrl(string subdomain, string clientId)
    {
        subdomain = subdomain.Trim().Replace("https://", "").Replace(".sharefile.com", "");
        return $"https://{subdomain}.sharefile.com/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
    }

    public async Task CompleteSignInAsync(string subdomain, string clientId, string clientSecret, string code, CancellationToken ct = default)
    {
        subdomain = subdomain.Trim().Replace("https://", "").Replace(".sharefile.com", "");
        code = code.Trim();

        var tokenUrl = $"https://{subdomain}.sharefile.com/oauth/token";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri
        });

        using var response = await _http.PostAsync(tokenUrl, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ShareFileAuthException($"ShareFile sign-in failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl))
        {
            throw new ShareFileAuthException($"ShareFile sign-in failed: {errorEl.GetString()}");
        }

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new ShareFileAuthException("ShareFile did not return an access token.");

        var apicp = root.TryGetProperty("apicp", out var apicpEl) ? apicpEl.GetString() : "sf-api.com";
        var accountSubdomain = root.TryGetProperty("subdomain", out var sdEl) ? sdEl.GetString() : subdomain;

        _baseUrl = $"https://{accountSubdomain}.{apicp}/sf/v3";
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        IsSignedIn = true;
    }

    private static readonly HashSet<string> SpecialFolderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "allshared", "favorites", "top", "connectors"
    };

    public async Task<string> GetRootFolderIdAsync(string? rootFolderPath, CancellationToken ct = default)
    {
        var value = string.IsNullOrWhiteSpace(rootFolderPath) ? "allshared" : rootFolderPath.Trim();

        if (SpecialFolderIds.Contains(value))
        {
            var json = await GetJsonAsync($"{_baseUrl}/Items({value})", ct);
            return json.GetProperty("Id").GetString()!;
        }

        var path = value.StartsWith("/") ? value : "/" + value;
        var encoded = Uri.EscapeDataString(path);
        var byPath = await GetJsonAsync($"{_baseUrl}/Items/ByPath?path={encoded}", ct);
        return byPath.GetProperty("Id").GetString()!;
    }

    public async Task<List<(string Id, string Name)>> GetSubfolderListAsync(string parentId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/Items({parentId})/Children?$select=Id,Name,FileName,FileCount&$top=1000";
        var json = await GetJsonAsync(url, ct);
        var result = new List<(string, string)>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            if (!IsFolderItem(item))
            {
                continue;
            }

            var id = item.GetProperty("Id").GetString() ?? "";
            var name = item.TryGetProperty("Name", out var n) && !string.IsNullOrEmpty(n.GetString())
                ? n.GetString()!
                : (item.TryGetProperty("FileName", out var fn) ? fn.GetString() ?? "" : "");
            result.Add((id, name));
        }
        return result.OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<ShareFileItemInfo>> GetChildrenAsync(string folderId, CancellationToken ct = default)
    {
        var select = "Id,Name,FileName,FileSizeBytes,FileCount,CreationDate,ClientModifiedDate,ProgenyEditDate," +
            "Creator/FirstName,Creator/LastName,Creator/Company,Creator/Email," +
            "Owner/FirstName,Owner/LastName,Owner/Company,Owner/Email";
        var url = $"{_baseUrl}/Items({folderId})/Children?$select={select}&$expand=Creator,Owner&$top=1000";
        var json = await GetJsonAsync(url, ct);

        var result = new List<ShareFileItemInfo>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            var isFolder = IsFolderItem(item);

            string name = item.TryGetProperty("FileName", out var fn) && !string.IsNullOrEmpty(fn.GetString())
                ? fn.GetString()!
                : (item.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "");

            var info = new ShareFileItemInfo
            {
                Id = item.GetProperty("Id").GetString() ?? "",
                Name = name,
                IsFolder = isFolder,
                FileSizeBytes = item.TryGetProperty("FileSizeBytes", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : null,
                CreationDate = TryGetDate(item, "CreationDate"),
                ModifiedDate = TryGetDate(item, "ClientModifiedDate") ?? TryGetDate(item, "ProgenyEditDate"),
                CreatorName = TryGetPersonName(item, "Creator"),
                CreatorCompany = TryGetPersonCompany(item, "Creator"),
                OwnerName = TryGetPersonName(item, "Owner")
            };
            result.Add(info);
        }
        return result;
    }

    public async Task<string> GetRawChildrenDebugJsonAsync(string folderId, CancellationToken ct = default)
    {
        // Deliberately the exact same $select/$expand as GetChildrenAsync, so this
        // dump reflects precisely what the real report call receives.
        var select = "Id,Name,FileName,FileSizeBytes,FileCount,CreationDate,ClientModifiedDate,ProgenyEditDate," +
            "Creator/FirstName,Creator/LastName,Creator/Company,Creator/Email," +
            "Owner/FirstName,Owner/LastName,Owner/Company,Owner/Email";
        var url = $"{_baseUrl}/Items({folderId})/Children?$select={select}&$expand=Creator,Owner&$top=3";
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return $"Request failed ({(int)response.StatusCode}):\n{body}";
        }

        using var doc = JsonDocument.Parse(body);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string?> GetItemWebLinkAsync(string itemId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"{_baseUrl}/Items({itemId})/Redirection", ct);
        if (json.TryGetProperty("Uri", out var uriEl))
        {
            return uriEl.GetString();
        }
        throw new InvalidOperationException("Redirection response did not contain a Uri field.");
    }

    private static bool IsFolderItem(JsonElement item)
    {
        if (item.TryGetProperty("odata.type", out var t))
        {
            var typeStr = t.GetString() ?? "";
            if (typeStr.Contains("Folder", StringComparison.OrdinalIgnoreCase)) return true;
            if (typeStr.Contains("File", StringComparison.OrdinalIgnoreCase)) return false;
        }

        // Fallback if the odata type annotation isn't present: only Folder items expose FileCount.
        return item.TryGetProperty("FileCount", out _);
    }

    private static DateTime? TryGetDate(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(el.GetString(), out var dt))
            {
                return dt;
            }
        }
        return null;
    }

    private static string? TryGetPersonName(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var person) || person.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var first = person.TryGetProperty("FirstName", out var f) ? f.GetString() : null;
        var last = person.TryGetProperty("LastName", out var l) ? l.GetString() : null;
        var full = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(full)) return full;

        return person.TryGetProperty("Email", out var e) ? e.GetString() : null;
    }

    private static string? TryGetPersonCompany(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var person) || person.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return person.TryGetProperty("Company", out var c) ? c.GetString() : null;
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ShareFile API request failed ({(int)response.StatusCode}) for {url}: {body}");
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    public void Dispose() => _http.Dispose();
}
