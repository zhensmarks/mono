using System.Collections.Generic;

namespace BMachine.UI.Models;

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";

    // --- Mode update lokal (LAN) ---

    /// <summary>True kalau info berasal dari folder update lokal, bukan GitHub.</summary>
    public bool IsLocalSource { get; set; }

    /// <summary>Folder sumber manifest (mis. \\ABENG\mono\updates).</summary>
    public string SourceFolder { get; set; } = "";

    /// <summary>Folder payload yang isinya disalin ke folder instalasi.</summary>
    public string PayloadFolder { get; set; } = "";

    /// <summary>Catatan rilis versi pendek: satu baris = satu poin.</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>SHA256 payload exe dari manifest (kosong = manifest lama tanpa hash).</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Ukuran payload dari manifest dalam byte (0 = tidak diketahui).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Ukuran payload siap tampil di banner (mis. "147 MB").</summary>
    public string SizeText => SizeBytes > 0
        ? (SizeBytes / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB"
        : "";

    /// <summary>Catatan rilis siap tampil (poin per baris).</summary>
    public string NotesText => Notes.Count > 0
        ? string.Join("\n", Notes)
        : ReleaseNotes;

    /// <summary>Catatan rilis satu baris (untuk strip banner).</summary>
    public string NotesInline => Notes.Count > 0
        ? string.Join("  •  ", Notes)
        : ReleaseNotes.Replace('\n', ' ');
}
