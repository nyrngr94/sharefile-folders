using System;
using System.Collections.Generic;
using ClosedXML.Excel;

namespace ShareFileDocumentIndex.App;

public static class ExcelReportWriter
{
    private static readonly string[] Headers =
    {
        "Kind", "Title", "Ownership", "ID", "Document Size", "Folder Path",
        "Added By", "Organization", "Added On", "Modified On", "Document Link"
    };

    public static void Write(string filePath, string clientName, List<ReportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Document Index");

        ws.Cell(1, 1).Value = $"{clientName}: Document Index";
        ws.Range(1, 1, 1, Headers.Length).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"As Of: {DateTime.Now:MMM d, yyyy h:mm tt}";
        ws.Range(2, 1, 2, Headers.Length).Merge();
        ws.Cell(2, 1).Style.Font.Italic = true;

        const int headerRow = 4;
        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        int r = headerRow + 1;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Kind;
            ws.Cell(r, 2).Value = row.Title;
            ws.Cell(r, 3).Value = row.Ownership;
            ws.Cell(r, 4).Value = row.Id;
            ws.Cell(r, 5).Value = row.DocumentSize;
            ws.Cell(r, 6).Value = row.FolderPath;
            ws.Cell(r, 7).Value = row.AddedBy;
            ws.Cell(r, 8).Value = row.Organization;
            ws.Cell(r, 9).Value = row.AddedOn;
            ws.Cell(r, 10).Value = row.ModifiedOn;

            if (!string.IsNullOrEmpty(row.DocumentLink))
            {
                ws.Cell(r, 11).SetHyperlink(new XLHyperlink(row.DocumentLink));
                ws.Cell(r, 11).Value = "Open";
            }

            r++;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, Headers.Length).AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 60);
        ws.Column(6).Width = Math.Min(ws.Column(6).Width, 60);

        workbook.SaveAs(filePath);
    }
}
