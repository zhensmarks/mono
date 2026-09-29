using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BMachine.UI.Models.MantraData;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BMachine.UI.Services.MantraData;

public class WordParserService
{
    public (List<string> Columns, List<TableDataRow> Rows) LoadWordDocx(string filePath)
    {
        var columns = new List<string> { "Kategori", "NAMA", "TTL", "ALAMAT", "JABATAN" };
        var rows = new List<TableDataRow>();

        if (!File.Exists(filePath)) return (columns, rows);

        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return (columns, rows);

        string currentCategory = "GURU";
        TableDataRow? currentRow = null;
        int rowNum = 1;

        foreach (var p in body.Elements<Paragraph>())
        {
            var text = p.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Check if line is a category header
            if (text.Equals("KEPSEK", StringComparison.OrdinalIgnoreCase) || 
                text.Equals("KEPALA SEKOLAH", StringComparison.OrdinalIgnoreCase))
            {
                currentCategory = "KEPSEK";
                continue;
            }
            if (text.Equals("GURU", StringComparison.OrdinalIgnoreCase) || 
                text.Equals("STAFF", StringComparison.OrdinalIgnoreCase))
            {
                currentCategory = "GURU";
                continue;
            }

            // Split key-value by ':'
            var parts = text.Split(':', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim().ToUpperInvariant();
                var val = parts[1].Trim();

                if (key.Contains("NAMA"))
                {
                    if (currentRow != null) rows.Add(currentRow);
                    currentRow = new TableDataRow { RowNumber = rowNum++ };
                    currentRow["Kategori"] = currentCategory;
                    currentRow["NAMA"] = val;
                }
                else if (currentRow != null)
                {
                    if (key.Contains("LAHIR") || key.Contains("TTL")) currentRow["TTL"] = val;
                    else if (key.Contains("ALAMAT")) currentRow["ALAMAT"] = val;
                    else if (key.Contains("JABATAN")) currentRow["JABATAN"] = val;
                    else
                    {
                        if (!columns.Contains(parts[0].Trim())) columns.Add(parts[0].Trim());
                        currentRow[parts[0].Trim()] = val;
                    }
                }
            }
        }

        if (currentRow != null) rows.Add(currentRow);

        return (columns, rows);
    }
}
