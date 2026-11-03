using System.Collections.Generic;
using System.Linq;

namespace PixelcutCompact.Models;

/// <summary>Satu bagian changelog untuk SATU versi (dipakai saat user tertinggal beberapa versi).</summary>
public class UpdateChangelogSection
{
    public string Version { get; set; } = "";
    public List<string> Notes { get; set; } = new();
}

/// <summary>Baris siap tampil di dialog update: pemisah versi (header) atau satu poin catatan.</summary>
public class UpdateNoteItem
{
    public bool IsHeader { get; init; }
    public string Text { get; init; } = "";
}

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

    /// <summary>Folder sumber manifest (mis. \\...\BMachine\PixaCompact).</summary>
    public string SourceFolder { get; set; } = "";

    /// <summary>Folder payload yang isinya disalin ke folder instalasi.</summary>
    public string PayloadFolder { get; set; } = "";

    /// <summary>Catatan rilis versi pendek: satu baris = satu poin.</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>Changelog versi yang belum dipasang (dari changelog.json), urut naik.</summary>
    public List<UpdateChangelogSection> Changelog { get; set; } = new();

    /// <summary>SHA256 payload exe dari manifest (kosong = manifest lama tanpa hash).</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Ukuran payload dari manifest dalam byte (0 = tidak diketahui).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Ukuran payload siap tampil (mis. "220 MB").</summary>
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

    /// <summary>Baris dialog update. Tergantung changelog: gabung per versi bila tertinggal &gt;1 versi.</summary>
    public List<UpdateNoteItem> BuildNoteItems(int singleSectionLimit = 4)
    {
        var items = new List<UpdateNoteItem>();

        if (Changelog.Count > 1)
        {
            foreach (var section in Changelog)
            {
                items.Add(new UpdateNoteItem { IsHeader = true, Text = $"------- v{section.Version} -------" });
                foreach (var note in section.Notes)
                    items.Add(new UpdateNoteItem { Text = note });
            }
            return items;
        }

        foreach (var note in Notes.Take(singleSectionLimit))
            items.Add(new UpdateNoteItem { Text = note });

        return items;
    }
}
