using System.Text.Json;
using BMachine.UI.Services;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Regresi mode update lokal (LAN): manifest.json dibaca dari folder share, versi dibandingkan
/// dengan versi aplikasi, dan catatan rilis singkat ikut terbaca.
/// </summary>
public class LocalUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bm-test-update-" + Guid.NewGuid().ToString("N"));

    public LocalUpdateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private void WriteManifest(object payload)
        => File.WriteAllText(Path.Combine(_folder, LocalUpdateService.ManifestFileName), JsonSerializer.Serialize(payload));

    private void WriteChangelog(object payload)
        => File.WriteAllText(Path.Combine(_folder, LocalUpdateService.ChangelogFileName), JsonSerializer.Serialize(payload));

    [Fact]
    public async Task VersiLebihBaruTerdeteksiSebagaiUpdate()
    {
        WriteManifest(new
        {
            version = "99.1.0",
            notes = new[] { "Checklist Trello auto-refresh", "Tombol refresh panel" },
            payload = "payload"
        });

        var (info, status) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("99.1.0", info.LatestVersion);
        Assert.Equal(2, info.Notes.Count);
        Assert.Contains("Checklist Trello auto-refresh", info.NotesText);
        Assert.Equal(Path.Combine(_folder, "payload"), info.PayloadFolder);
        Assert.Contains("99.1.0", status);
    }

    [Fact]
    public async Task VersiSamaTidakMenawarkanUpdate()
    {
        WriteManifest(new { version = LocalUpdateService.GetCurrentVersion(), notes = new[] { "x" } });

        var (info, status) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.False(info.IsUpdateAvailable);
        Assert.Contains("terbaru", status);
    }

    [Fact]
    public async Task VersiLebihLamaTidakMenawarkanUpdate()
    {
        WriteManifest(new { version = "0.0.1", notes = new[] { "x" } });

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task PrefixVDiVersiDiabaikan()
    {
        WriteManifest(new { version = "v99.2.0", notes = new[] { "x" } });

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("99.2.0", info.LatestVersion);
    }

    [Fact]
    public async Task CatatanRilisBolehStringMultiBaris()
    {
        WriteManifest(new { version = "99.3.0", notes = "- Poin satu\n* Poin dua\n\nPoin tiga" });

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.Equal(new[] { "Poin satu", "Poin dua", "Poin tiga" }, info.Notes);
    }

    [Fact]
    public async Task FolderTanpaManifestTidakMelempar()
    {
        var (info, status) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.False(info.IsUpdateAvailable);
        Assert.Contains("manifest.json", status);
    }

    [Fact]
    public async Task FolderTidakAdaTidakMelempar()
    {
        var (info, status) = await LocalUpdateService.CheckAsync(Path.Combine(_folder, "tidak-ada"), allowFallback: false);

        Assert.False(info.IsUpdateAvailable);
        Assert.False(string.IsNullOrWhiteSpace(status));
    }

    [Theory]
    [InlineData(null, LocalUpdateService.DefaultFolder)]
    [InlineData("", LocalUpdateService.DefaultFolder)]
    [InlineData("   ", LocalUpdateService.DefaultFolder)]
    [InlineData(@"\\SERVER\share\updates", @"\\SERVER\share\updates")]
    [InlineData("  D:\\mono\\updates  ", "D:\\mono\\updates")]
    public void ResolveFolderMemakaiDefaultKalauKosong(string? configured, string expected)
    {
        Assert.Equal(expected, LocalUpdateService.ResolveFolder(configured));
    }

    [Fact]
    public async Task ApplyMenolakPayloadTanpaExe()
    {
        WriteManifest(new { version = "99.4.0", notes = new[] { "x" }, payload = "payload" });
        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        var (ok, message) = await LocalUpdateService.ApplyAsync(info);

        Assert.False(ok);
        Assert.Contains("payload", message);
    }

    [Fact]
    public async Task ApplyDibatalkanKalauHashPayloadTidakCocok()
    {
        WriteManifest(new { version = "99.5.0", notes = new[] { "x" }, payload = "payload", sha256 = "DEADBEEF" });
        var payloadDir = Path.Combine(_folder, "payload");
        Directory.CreateDirectory(payloadDir);
        await File.WriteAllTextAsync(Path.Combine(payloadDir, "BMachine.App.exe"), "bukan exe asli");

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);
        var (ok, message) = await LocalUpdateService.ApplyAsync(info);

        Assert.False(ok);
        Assert.Contains("SHA256", message);
    }

    // --- Changelog gabungan (user tertinggal beberapa versi) ---

    [Fact]
    public async Task ChangelogMenggabungkanVersiYangKetinggalanUrutNaik()
    {
        WriteManifest(new { version = "99.9.0", notes = new[] { "catatan manifest" } });
        WriteChangelog(new object[]
        {
            new { version = "99.9.0", notes = new[] { "poin sembilan" } },
            new { version = "99.8.0", notes = new[] { "poin delapan" } },
            new { version = "0.0.1", notes = new[] { "versi jauh lebih lama dari app" } },
        });

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        // Hanya versi > versi app dan <= versi manifest, urut naik.
        Assert.Equal(new[] { "99.8.0", "99.9.0" }, info.Changelog.Select(s => s.Version));

        var items = info.BuildNoteItems();

        Assert.True(items[0].IsHeader);
        Assert.Equal("------- v99.8.0 -------", items[0].Text);
        Assert.False(items[1].IsHeader);
        Assert.Equal("poin delapan", items[1].Text);
        Assert.True(items[2].IsHeader);
        Assert.Equal("------- v99.9.0 -------", items[2].Text);
        Assert.Equal("poin sembilan", items[3].Text);
        Assert.Equal(4, items.Count);
    }

    [Fact]
    public async Task TanpaChangelogPakaiCatatanManifestTanpaHeader()
    {
        WriteManifest(new { version = "99.10.0", notes = new[] { "satu", "dua", "tiga", "empat", "lima" } });

        var (info, _) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.Empty(info.Changelog);

        var items = info.BuildNoteItems();

        Assert.Equal(4, items.Count);                      // satu versi: tetap dibatasi 4 poin
        Assert.All(items, item => Assert.False(item.IsHeader));
        Assert.Equal("satu", items[0].Text);
    }

    [Fact]
    public async Task ChangelogRusakTidakMelemparDanTetapPakaiManifest()
    {
        WriteManifest(new { version = "99.11.0", notes = new[] { "aman" } });
        File.WriteAllText(Path.Combine(_folder, LocalUpdateService.ChangelogFileName), "{ bukan json ]");

        var (info, status) = await LocalUpdateService.CheckAsync(_folder, allowFallback: false);

        Assert.True(info.IsUpdateAvailable);
        Assert.Empty(info.Changelog);
        Assert.Single(info.BuildNoteItems());
        Assert.Contains("99.11.0", status);
    }
}
