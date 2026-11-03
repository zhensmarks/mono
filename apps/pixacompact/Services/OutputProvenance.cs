using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PixelcutCompact.Services;

/// <summary>
/// Indeks bukti output: mencatat bahwa output X benar-benar berasal dari input Y
/// (path + ukuran + mtime input saat diproses). Dipakai saat memutuskan skip:
/// file output TANPA record tidak dipercaya lagi (diproses ulang sekali), sehingga
/// output salah dari run lama (bug mapping historis) otomatis diperbaiki.
/// Disimpan di output-index.json di folder exe (bersama settings.json).
/// </summary>
public static class OutputProvenance
{
    public sealed class Entry
    {
        public string Input { get; set; } = "";
        public long InputSize { get; set; }
        public long InputMtimeTicks { get; set; }
        public long OutputSize { get; set; }
    }

    private static readonly object _lock = new();
    private static Dictionary<string, Entry>? _cache;
    private static bool _dirty;

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "output-index.json");

    private static Dictionary<string, Entry> Load()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    var doc = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath));
                    if (doc != null)
                        foreach (var kv in doc)
                            _cache[kv.Key] = kv.Value;
                }
            }
            catch { /* index rusak = anggap kosong; semua output diproses ulang sekali (aman) */ }
            return _cache;
        }
    }

    /// <summary>
    /// True HANYA kalau record membuktikan output ini berasal dari input ini
    /// (input tidak berubah sejak diproses) dan file output masih valid (≥1024 byte).
    /// Tanpa record = tidak dipercaya → diproses ulang.
    /// </summary>
    public static bool ShouldSkip(string inputPath, string outputPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath)) return false;
            if (!File.Exists(outputPath)) return false;
            var outInfo = new FileInfo(outputPath);
            if (outInfo.Length < 1024) return false;

            var map = Load();
            if (!map.TryGetValue(outputPath, out var entry)) return false; // output lama tanpa record
            if (!string.Equals(entry.Input, inputPath, StringComparison.OrdinalIgnoreCase)) return false;

            var inInfo = new FileInfo(inputPath);
            if (!inInfo.Exists) return false;
            if (inInfo.Length != entry.InputSize) return false;                    // input diganti
            if (inInfo.LastWriteTimeUtc.Ticks != entry.InputMtimeTicks) return false; // input diedit
            return true;
        }
        catch { return false; }
    }

    /// <summary>Catat bahwa output baru saja ditulis dari input ini. Panggil setelah penulisan sukses.</summary>
    public static void Record(string inputPath, string outputPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath)) return;
            var inInfo = new FileInfo(inputPath);
            var outInfo = new FileInfo(outputPath);
            if (!inInfo.Exists || !outInfo.Exists) return;

            var map = Load();
            map[outputPath] = new Entry
            {
                Input = inputPath,
                InputSize = inInfo.Length,
                InputMtimeTicks = inInfo.LastWriteTimeUtc.Ticks,
                OutputSize = outInfo.Length
            };
            lock (_lock) { _dirty = true; }
        }
        catch { /* pencatatan gagal bukan fatal: run berikutnya hanya mengulang satu file */ }
    }

    /// <summary>Simpan ke disk kalau ada perubahan. Panggil di akhir proses (finally).</summary>
    public static void SaveIfDirty()
    {
        lock (_lock)
        {
            if (!_dirty || _cache == null) return;
            try
            {
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = false });
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                File.Move(tmp, FilePath, overwrite: true); // atomic: gagal tulis tidak merusak index lama
                _dirty = false;
            }
            catch { /* disk penuh/di-lock: biarkan, save berikutnya mencoba lagi */ }
        }
    }
}
