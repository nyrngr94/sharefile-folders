using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareFileDocumentIndex.App;

public sealed class ReportGenerator
{
    private readonly ShareFileClient _client;
    private readonly bool _includeDocumentLinks;

    public string? LinkFetchError { get; private set; }

    public ReportGenerator(ShareFileClient client, bool includeDocumentLinks)
    {
        _client = client;
        _includeDocumentLinks = includeDocumentLinks;
    }

    public async Task<List<ReportRow>> BuildAsync(string rootFolderId, string rootFolderDisplayName, IProgress<string>? progress, CancellationToken ct = default)
    {
        var rows = new List<ReportRow>();
        await WalkAsync(rootFolderId, "/" + rootFolderDisplayName, rows, progress, ct);
        return rows;
    }

    private async Task WalkAsync(string folderId, string folderPath, List<ReportRow> rows, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"Scanning {folderPath}...");
        var children = await _client.GetChildrenAsync(folderId, ct);

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();

            var row = new ReportRow
            {
                Kind = child.IsFolder ? "Folder" : GetKind(child.Name),
                Title = child.Name,
                Ownership = child.OwnerName ?? child.CreatorName ?? "",
                Id = child.Id,
                DocumentSize = child.IsFolder ? "" : FormatSize(child.FileSizeBytes),
                FolderPath = folderPath,
                AddedBy = child.CreatorName ?? "",
                Organization = child.CreatorCompany ?? "",
                AddedOn = child.CreationDate?.ToString("MMM d, yyyy h:mm tt") ?? "",
                ModifiedOn = child.ModifiedDate?.ToString("MMM d, yyyy h:mm tt") ?? "",
                DocumentLink = ""
            };

            if (_includeDocumentLinks && LinkFetchError is null)
            {
                try
                {
                    row.DocumentLink = await _client.GetItemWebLinkAsync(child.Id, ct) ?? "";
                }
                catch (Exception ex)
                {
                    LinkFetchError = ex.Message;
                }
            }

            rows.Add(row);

            if (child.IsFolder)
            {
                await WalkAsync(child.Id, folderPath + "/" + child.Name, rows, progress, ct);
            }
        }
    }

    private static string GetKind(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return "File";
        return ext.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null) return "";
        double size = bytes.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }
}
