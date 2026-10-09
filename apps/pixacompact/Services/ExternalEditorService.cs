using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PixelcutCompact.Services;

/// <summary>Pilihan aplikasi editor eksternal yang tersedia sebagai preset.</summary>
public enum ExternalEditorKind
{
    Photoshop,
    Photocraft,
    Custom
}

/// <summary>
/// Menjalankan editor gambar eksternal (Photoshop, PhotoCraft, atau aplikasi
/// custom lain) untuk membuka file hasil + file asli. Dipakai saat tombol Mode
/// Edit ditekan padahal editor bawaan (EditorBetaMode) sedang OFF — sebagai
/// "jembatan" agar user tetap bisa mengedit hasil di editor profesional.
/// </summary>
public static class ExternalEditorService
{
    /// <summary>Parse string kind dari settings ke enum (case-insensitive).</summary>
    public static ExternalEditorKind ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return ExternalEditorKind.Photocraft;
        return Enum.TryParse<ExternalEditorKind>(kind, ignoreCase: true, out var k)
            ? k : ExternalEditorKind.Photocraft;
    }

    /// <summary>
    /// Resolve executable PhotoCraft dari path yang diatur user.
    /// Path boleh berupa file .exe langsung (cara yang dianjurkan), atau
    /// folder repo PhotoCraft (kompatibilitas lama) — kalau folder, dicari
    /// binary hasil build di dalamnya. Mengembalikan path atau null.
    /// </summary>
    public static string? ResolvePhotocraftPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (File.Exists(path)) return path;

        if (Directory.Exists(path))
        {
            var exeName = OperatingSystem.IsWindows() ? "photocraft.exe" : "photocraft";
            var candidates = new[]
            {
                Path.Combine(path, "target", "release", exeName),
                Path.Combine(path, "target", "debug", exeName),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>
    /// Jalankan editor eksternal dengan membuka <paramref name="resultPath"/>
    /// (file hasil) dan <paramref name="originalPath"/> (file asli). Untuk
    /// Photoshop, dibungkus skrip JSX yang membuka kedua file sebagai layer;
    /// untuk editor lain (termasuk PhotoCraft), dibuka langsung sebagai argumen
    /// file (banyak editor mendukung multiple args).
    /// </summary>
    public static bool Launch(
        ExternalEditorKind kind,
        string? editorPath,
        string resultPath,
        string originalPath,
        out string error)
    {
        error = "";
        if (kind == ExternalEditorKind.Photocraft)
        {
            // PhotoCraft diperlakukan seperti editor lain: user memilih file .exe.
            var exe = ResolvePhotocraftPath(editorPath);
            if (exe == null)
            {
                error = "Path PhotoCraft belum diatur. Buka Preferensi → Umum untuk memilih executable PhotoCraft (photocraft.exe).";
                return false;
            }
            return LaunchGeneric(exe, new[] { resultPath, originalPath }, out error);
        }

        if (kind == ExternalEditorKind.Photoshop)
        {
            if (string.IsNullOrEmpty(editorPath) || !File.Exists(editorPath))
            {
                error = "Path Photoshop belum diatur. Buka Preferensi → Umum untuk memilih executable Photoshop.";
                return false;
            }
            return LaunchPhotoshop(editorPath, resultPath, originalPath, out error);
        }

        // Custom
        if (string.IsNullOrEmpty(editorPath) || !File.Exists(editorPath))
        {
            error = "Path editor custom belum diatur. Buka Preferensi → Umum untuk memilih executable.";
            return false;
        }
        return LaunchGeneric(editorPath, new[] { resultPath, originalPath }, out error);
    }

    /// <summary>Jalankan editor generik dengan beberapa path file sebagai argumen.</summary>
    private static bool LaunchGeneric(string exe, IReadOnlyList<string> files, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true
            };
            foreach (var f in files)
                psi.ArgumentList.Add(f);

            var p = Process.Start(psi);
            if (p == null)
            {
                error = "Tidak bisa memulai proses editor.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Jalankan Photoshop dengan skrip JSX yang membuka PNG hasil sebagai layer
    /// utama, lalu menempelkan file asli sebagai layer referensi (logik lama).
    /// </summary>
    private static bool LaunchPhotoshop(string photoshopExe, string resultPath, string originalPath, out string error)
    {
        error = "";
        var escapedResult = resultPath.Replace("\\", "\\\\").Replace("'", "\\'");
        var escapedOriginal = originalPath.Replace("\\", "\\\\").Replace("'", "\\'");

        var jsx = $@"
#target photoshop
try {{
    var pngFile = new File('{escapedResult}');
    if (pngFile.exists) {{
        var doc = app.open(pngFile);
        if (doc.artLayers.length > 0) {{ doc.artLayers[0].name = 'Hasil (Masker Transparan)'; }}
        var jpgFile = new File('{escapedOriginal}');
        if (jpgFile.exists) {{
            var jpgDoc = app.open(jpgFile);
            jpgDoc.selection.selectAll();
            jpgDoc.activeLayer.copy();
            jpgDoc.close(SaveOptions.DONOTSAVECHANGES);
            app.activeDocument = doc;
            var pastedLayer = doc.paste();
            pastedLayer.name = 'Referensi Asli (Original)';
            pastedLayer.move(doc, ElementPlacement.PLACEATEND);
            doc.activeLayer = doc.artLayers[0];
        }}
    }} else {{
        alert('File hasil tidak ditemukan:\n' + '{escapedResult}');
    }}
}} catch (e) {{
    alert('Error running Photoshop script:\n' + e.message);
}}
";

        var tempJsxPath = Path.Combine(Path.GetTempPath(), $"pixelcut_open_{Guid.NewGuid():N}".Substring(0, 18) + ".jsx");
        try
        {
            File.WriteAllText(tempJsxPath, jsx);
            var psi = new ProcessStartInfo
            {
                FileName = photoshopExe,
                Arguments = $"\"{tempJsxPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
