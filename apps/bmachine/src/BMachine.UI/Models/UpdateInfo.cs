using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Models;

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

    /// <summary>
    /// Kata depan (sebelum ":" di catatan, mis. "Pasfoto" / "Update"). Dipakai XAML
    /// untuk menyorotnya (bold + warna aksen) supaya apa yang di-update terbaca sekilas.
    /// null = tidak ada bagian yang disorot (tampilan teks biasa).
    /// </summary>
    public string? Highlight { get; init; }

    /// <summary>Isi catatan setelah kata depan (mis. " PFM/PFB tanpa PSD spesifik → ...").</summary>
    public string Rest { get; init; } = "";

    /// <summary>True kalau catatan punya bagian yang disorot ("sasaran: perubahan").</summary>
    public bool HasHighlight => !string.IsNullOrEmpty(Highlight);

    /// <summary>True kalau catatan polos (tanpa prefix "sasaran:"). Dipakai XAML:
    /// fallback teks biasa hanya tampil kalau <see cref="HasHighlight"/> false.</summary>
    public bool HasPlain => string.IsNullOrEmpty(Highlight);
}

public class ManifestComponentInfo
{
    public string Name { get; set; } = "";
    public bool Changed { get; set; }
    public bool Included { get; set; } = true;
    public List<string> Paths { get; set; } = new();

    /// <summary>
    /// Untuk binding UI: gabungan path yang relevan (tampil di panel komponen,
    /// tooltip). Kosong = path tidak diketahui.
    /// </summary>
    public string PathsDisplay => Paths == null || Paths.Count == 0
        ? ""
        : Paths.Count == 1 ? Paths[0] : string.Join(", ", Paths.Take(2)) + (Paths.Count > 2 ? ", …" : "");
    public bool HasMultiplePaths => Paths != null && Paths.Count > 1;
}

public class ManifestCategories
{
    public string Version { get; set; } = "";
    public ManifestComponentInfo[] Components { get; set; } =
        Array.Empty<ManifestComponentInfo>();

    public bool AnyChanged() => Components.Any(c => c != null && c.Changed);

    /// <summary>Kategori default: semua komponen yang diikutkan dicentang.</summary>
    public static string[] DefaultSelected(IEnumerable<ManifestComponentInfo> changed)
    {
        var names = new List<string>();
        foreach (var c in changed)
            if (c != null && c.Changed && c.Included) names.Add(c.Name);
        return names.ToArray();
    }
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

    /// <summary>Folder sumber manifest (mis. \\ABENG\mono\updates).</summary>
    public string SourceFolder { get; set; } = "";

    /// <summary>Folder payload yang isinya disalin ke folder instalasi.</summary>
    public string PayloadFolder { get; set; } = "";

    /// <summary>Catatan rilis versi pendek: satu baris = satu poin.</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>
    /// Changelog versi-versi yang BELUM dipasang user (dari changelog.json di folder update),
    /// urut naik: versi terlama dulu. Kosong = changelog.json tidak ada → pakai <see cref="Notes"/>.
    /// </summary>
    public List<UpdateChangelogSection> Changelog { get; set; } = new();

    /// <summary>SHA256 payload exe dari manifest (kosong = manifest lama tanpa hash).</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Ukuran payload dari manifest dalam byte (0 = tidak diketahui).</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Komponen update (Aplikasi / Master / Action / Opsi). Kosong untuk manifest lama
    /// tanpa field ini — app tetap menjalankan update apa adanya (perilaku lama).
    /// </summary>
    public List<ManifestComponentInfo> Components { get; set; } = new();

    /// <summary>Channel update yang sedang dipakai ("stable"/"experimental"). Default "stable".</summary>
    public string Channel { get; set; } = "stable";

    /// <summary>Label channel untuk tampilan (mis. badge kecil di dialog update).</summary>
    public string ChannelLabel => string.Equals(Channel, "experimental", StringComparison.OrdinalIgnoreCase)
        ? "EXPERIMENTAL" : "STABLE";

    /// <summary>Daftar nama komponen yang tersedia di dialog (yang CHANGED di manifest).</summary>
    public List<ManifestComponentInfo> ChangedComponents =>
        Components.Where(c => c != null && c.Changed && c.Included).ToList();

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

    /// <summary>
    /// Baris dialog update. Kalau user tertinggal LEBIH DARI SATU versi, catatan digabung
    /// per versi: header <c>------- vX -------</c> lalu poin-poinnya (urut naik, tanpa
    /// pemotongan). Kalau hanya satu versi, tampil seperti sebelumnya (poin biasa, maks 4).
    /// Catatan format "<sasaran>: <perubahan>" — kata depannya disorot (Highlight),
    /// sisanya di Rest; catatan tanpa ":" tampil polos.
    /// </summary>
    public List<UpdateNoteItem> BuildNoteItems(int singleSectionLimit = 4)
    {
        var items = new List<UpdateNoteItem>();

        static (string highlight, string rest) SplitNote(string note)
        {
            var idx = note.IndexOf(':');
            if (idx > 0 && idx < note.Length - 1)
                return (note.Substring(0, idx).Trim(), note.Substring(idx + 1).Trim());
            return (string.Empty, note.Trim());
        }

        if (Changelog.Count > 1)
        {
            foreach (var section in Changelog)
            {
                items.Add(new UpdateNoteItem { IsHeader = true, Text = $"------- v{section.Version} -------" });
                foreach (var note in section.Notes)
                {
                    var (hl, rest) = SplitNote(note);
                    items.Add(new UpdateNoteItem { Text = note, Highlight = hl, Rest = rest });
                }
            }
            return items;
        }

        foreach (var note in Notes.Take(singleSectionLimit))
        {
            var (hl, rest) = SplitNote(note);
            items.Add(new UpdateNoteItem { Text = note, Highlight = hl, Rest = rest });
        }

        return items;
    }
}
