using System.Xml.Linq;
using BMachine.UI.Models;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Regresi dialog popup update (UpdateDialogWindow, menggantikan strip banner lama di MainWindow):
/// harus menampilkan versi, ukuran payload, catatan rilis berbutir, progres saat memasang,
/// dan tiga aksi (perbarui / nanti / tutup).
/// </summary>
public class UpdateBannerTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument ReadAxaml(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return XDocument.Parse(File.ReadAllText(Path.Combine(current.FullName, relativePath)));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mono repository root from the test output directory.");
    }

    private static string Banner()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/Dialogs/UpdateDialogWindow.axaml");

        var banner = doc.Descendants().First()
            ?? throw new Xunit.Sdk.XunitException("Jendela dialog update tidak ditemukan.");

        return banner.ToString();
    }

    [Fact]
    public void BannerMenampilkanVersiDanUkuranPayload()
    {
        var xaml = Banner();

        Assert.Contains("LatestVersion", xaml);
        Assert.Contains("UpdateSizeText", xaml);
        Assert.Contains("PEMBARUAN TERSEDIA", xaml);
    }

    [Fact]
    public void BannerPunyaDaftarCatatanBerbutir()
    {
        var doc = XDocument.Parse(Banner());

        var items = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ItemsControl");

        Assert.NotNull(items);
        Assert.Contains("UpdateNoteItems", items!.Attribute("ItemsSource")?.Value ?? "");
    }

    [Fact]
    public void BannerPunyaProgressSaatMemasang()
    {
        var xaml = Banner();

        Assert.Contains("ProgressBar", xaml);
        Assert.Contains("IsApplyingUpdate", xaml);
        Assert.Contains("UpdateActionStatus", xaml);
    }

    [Fact]
    public void BannerPunyaTigaAksi()
    {
        var xaml = Banner();

        Assert.Contains("ApplyUpdateCommand", xaml);
        Assert.Contains("Perbarui sekarang", xaml);
        Assert.Contains("DismissUpdateCommand", xaml);
        Assert.Contains("Nanti", xaml);
        Assert.Contains("IconX", xaml);
    }

    /// <summary>Keluhan: "tombol X kenapa ada 2". Dialog cuma boleh punya satu X (tutup).</summary>
    [Fact]
    public void HanyaAdaSatuTombolX()
    {
        var doc = XDocument.Parse(Banner());

        var closeButtons = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .Where(b => b.Descendants().Any(d =>
                d.Attribute("Data")?.Value.Contains("IconX") == true))
            .ToList();

        Assert.Equal(1, closeButtons.Count);
    }

    /// <summary>Keluhan: "ukuran tombol Nanti berbeda dengan tombol update". Keduanya wajib sama.</summary>
    [Fact]
    public void TombolAksiPunyaTinggiSama()
    {
        var doc = XDocument.Parse(Banner());

        var apply = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "Button" &&
            (e.Attribute("Command")?.Value.Contains("ApplyUpdateCommand") == true));
        var dismiss = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "Button" &&
            (e.Attribute("Command")?.Value.Contains("DismissUpdateCommand") == true));

        Assert.NotNull(apply);
        Assert.NotNull(dismiss);

        // DESIGN_SYSTEM.md §4: tombol pakai MinHeight 36 (BUKAN Height) + Padding 16,8.
        var applyMinHeight = apply!.Attribute("MinHeight")?.Value
            ?? throw new Xunit.Sdk.XunitException("Tombol Perbarui sekarang tanpa MinHeight eksplisit.");
        var dismissMinHeight = dismiss!.Attribute("MinHeight")?.Value
            ?? throw new Xunit.Sdk.XunitException("Tombol Nanti tanpa MinHeight eksplisit.");

        Assert.Equal("36", applyMinHeight);
        Assert.Equal(applyMinHeight, dismissMinHeight);
        Assert.Equal(apply!.Attribute("Padding")?.Value, dismiss!.Attribute("Padding")?.Value);
    }

    /// <summary>Keluhan: popup muncul di belakang jendela utama sehingga tombolnya tidak bisa diklik.</summary>
    [Fact]
    public void DialogSelaluDiDepanJendelaUtama()
    {
        var doc = XDocument.Parse(Banner());

        Assert.Equal("True", doc.Root?.Attribute("Topmost")?.Value);
    }

    /// <summary>Kalau user memilih "Nanti", pembaruan tetap harus terlihat lewat baris
    /// di flyout profil (Terminal/Settings/Log Out) — tanpa mengulang cek jaringan.</summary>
    [Fact]
    public void FlyoutProfilPunyaBarisPembaruan()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/DashboardView.axaml");

        var badge = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .Where(b => (b.Attribute("IsVisible")?.Value ?? "").Contains("HasPendingUpdate"))
            .ToList();

        var row = Assert.Single(badge);
        Assert.Contains("OpenPendingUpdateCommand", row.Attribute("Command")?.Value ?? "");
    }

    [Fact]
    public void SizeTextDiformatJadiMb()
    {
        Assert.Equal("147.5 MB", new UpdateInfo { SizeBytes = 154698856 }.SizeText);
    }

    [Fact]
    public void SizeTextKosongKalauManifestTidakPunyaUkuran()
    {
        Assert.Equal("", new UpdateInfo().SizeText);
    }
}
