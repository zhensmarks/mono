using System.Xml.Linq;
using BMachine.UI.Models;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Regresi banner update di MainWindow: harus menampilkan versi, ukuran payload, catatan rilis
/// berbutir, progres saat memasang, dan tiga aksi (perbarui / nanti / tutup).
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
        var doc = ReadAxaml("apps/bmachine/src/BMachine.App/Views/MainWindow.axaml");

        var banner = doc.Descendants()
            .FirstOrDefault(e => (e.Attribute("Name")?.Value ?? e.Attribute(Xaml + "Name")?.Value) == "Part_UpdateBanner")
            ?? throw new Xunit.Sdk.XunitException("Banner update (Part_UpdateBanner) tidak ditemukan di MainWindow.axaml.");

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
