using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

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
}

public sealed class ShareFileAuthException : Exception
{
    public ShareFileAuthException(string message) : base(message) { }
}

public sealed class ShareFileClient : IDisposable
{
    private readonly HttpClient _http = new();
    private string _baseUrl = "";

    public bool IsSignedIn { get; private set; }

    public async Task SignInAsync(string subdomain, string clientId, string clientSecret, string username, string password, CancellationToken ct = default)
    {
        subdomain = subdomain.Trim().Replace("https://", "").Replace(".sharefile.com", "");
        var tokenUrl = $"https://{subdomain}.sharefile.com/oauth/token";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["username"] = username,
            ["password"] = password
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

    public async Task<string> GetHomeFolderIdAsync(string? rootFolderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolderPath))
        {
            var json = await GetJsonAsync($"{_baseUrl}/Items(home)", ct);
            return json.GetProperty("Id").GetString()!;
        }

        var path = rootFolderPath.Trim();
        if (!path.StartsWith("/")) path = "/" + path;
        var encoded = Uri.EscapeDataString(path);
        var byPath = await GetJsonAsync($"{_baseUrl}/Items/ByPath?path={encoded}", ct);
        return byPath.GetProperty("Id").GetString()!;
    }

    public async Task<List<(string Id, string Name)>> GetSubfolderListAsync(string parentId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/Items({parentId})/Children?$select=Id,Name,FileName&$top=1000";
        var json = await GetJsonAsync(url, ct);
        var result = new List<(string, string)>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            var odataType = item.TryGetProperty("odata.type", out var t) ? t.GetString() ?? "" : "";
            if (!odataType.Contains("Folder", StringComparison.OrdinalIgnoreCase))
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
        var select = "Id,Name,FileName,FileSizeBytes,CreationDate,ClientModifiedDate,ProgenyEditDate";
        var url = $"{_baseUrl}/Items({folderId})/Children?$select={select}&$expand=Creator&$top=1000";
        var json = await GetJsonAsync(url, ct);

        var result = new List<ShareFileItemInfo>();
        foreach (var item in json.GetProperty("value").EnumerateArray())
        {
            var odataType = item.TryGetProperty("odata.type", out var t) ? t.GetString() ?? "" : "";
            var isFolder = odataType.Contains("Folder", StringComparison.OrdinalIgnoreCase);

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
                CreatorCompany = TryGetPersonCompany(item, "Creator")
            };
            result.Add(info);
        }
        return result;
    }

    public async Task<string?> GetItemWebLinkAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var json = await GetJsonAsync($"{_baseUrl}/Items({itemId})/Redirection", ct);
            if (json.TryGetProperty("Uri", out var uriEl))
            {
                return uriEl.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
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
