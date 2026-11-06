using Avalonia.Media;

namespace BMachine.UI.Models;

public class BatchScriptOption
{
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = ""; // Original Filename
    public string Path { get; set; } = "";
    public StreamGeometry? IconGeometry { get; set; }

    /// <summary>
    /// Tooltip tombol di baris master tab BATCH: menyebut skrip master yang mana.
    /// Nama alias dipakai apa adanya kalau memang beda dari nama file; kalau sama,
    /// cukup tampilkan nama itu sekali (tidak "MANASIK (manasik.py)").
    /// </summary>
    public string ToolTipText
    {
        get
        {
            string file = System.IO.Path.GetFileName(Path);
            if (string.IsNullOrWhiteSpace(file)) file = OriginalName;
            if (string.IsNullOrWhiteSpace(Name)) return file;

            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            return string.Equals(Name, stem, StringComparison.OrdinalIgnoreCase)
                ? Name
                : $"{Name} ({file})";
        }
    }
}
