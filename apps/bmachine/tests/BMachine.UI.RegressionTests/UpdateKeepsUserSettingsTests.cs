using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Pengaturan user TIDAK boleh disimpan di folder instalasi: payload update menimpa
/// `Scripts\...`, jadi urutan/nama skrip (dan tata letak radial yang ikut file itu)
/// akan balik ke bawaan tiap update. Keluhan user: "sudah atur pengaturan/tata letak
/// radial, saat update jadi berubah".
/// </summary>
public class UpdateKeepsUserSettingsTests
{
    private const string SeedPath = "Path.Combine(AppDomain.CurrentDomain.BaseDirectory, \"Scripts\", \"scripts.json\")";

    [Fact]
    public void ScriptOrderIsReadAndWrittenInAppData()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/SettingsViewModel.cs");

        var loadMethod = ExtractMethod(src, "public void LoadScriptOrder()");
        Assert.Contains("var path = _metadataPath;", loadMethod);
        Assert.Contains("File.Copy(seed, path);", loadMethod);   // seed bawaan, sekali saja

        var saveMethod = ExtractMethod(src, "private void SaveScriptOrder()");
        Assert.Contains("var path = _metadataPath;", saveMethod);
        Assert.DoesNotContain("BaseDirectory", saveMethod);
    }

    [Fact]
    public void InstallFolderScriptsJsonIsOnlyUsedAsSeed()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/ViewModels/SettingsViewModel.cs");

        var uses = Regex.Matches(src, Regex.Escape(SeedPath));
        Assert.Equal(2, uses.Count);   // LoadMetadata + LoadScriptOrder

        foreach (Match m in uses)
        {
            // Setiap pemakaian harus diikuti File.Copy (menanam bawaan), bukan penulisan.
            var window = src.Substring(m.Index, Math.Min(600, src.Length - m.Index));
            Assert.Contains("File.Copy(", window);
            Assert.DoesNotContain("File.WriteAllText(", window);
        }
    }

    [Fact]
    public void UpdateApplierCopiesPayloadWithoutMirroring()
    {
        var src = ReadRepoFile("apps/bmachine/src/BMachine.UI/Services/LocalUpdateService.cs");

        // Additif: file user yang tidak ada di payload (plugins, script impor, DB) tetap ada.
        Assert.Contains("Copy-Item -Path (Join-Path $payload '*'", src);
        Assert.DoesNotContain("/MIR", src);
    }

    /// <summary>Ambil badan method sampai kurung tutup sejajar (source-based test).</summary>
    private static string ExtractMethod(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method '{signature}' tidak ditemukan");

        int i = src.IndexOf('{', start);
        Assert.True(i > 0, $"body '{signature}' tidak ditemukan");

        int depth = 0;
        for (int k = i; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(i, k - i + 1);
            }
        }

        throw new InvalidOperationException($"body '{signature}' tidak seimbang");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new FileNotFoundException($"{relativePath} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}
