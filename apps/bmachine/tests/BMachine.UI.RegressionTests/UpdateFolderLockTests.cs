using System.Xml.Linq;
using BMachine.UI.Services;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Regresi: folder update di Settings dikunci untuk user. Kalau isinya dikosongkan atau
/// diubah jadi path relatif/sampah, aplikasi harus tetap menemukan sumber update (default NAS).
/// </summary>
public class UpdateFolderLockTests
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

    [Theory]
    [InlineData("asdf")]
    [InlineData("..\\updates")]
    [InlineData("updates")]
    [InlineData("folder update saya")]
    public void PathRelatifDitolakDanPakaiDefault(string configured)
    {
        Assert.Equal(LocalUpdateService.DefaultFolder, LocalUpdateService.ResolveFolder(configured));
    }

    [Theory]
    [InlineData(@"\\SERVER\share\updates")]
    [InlineData(@"D:\mono\updates")]
    [InlineData("U:\\#MASTER DESIGN\\SOFTWARE")]
    public void PathAbsolutTetapDipakai(string configured)
    {
        Assert.Equal(configured, LocalUpdateService.ResolveFolder(configured));
    }

    [Fact]
    public void KandidatSelaluMemuatDefaultNas()
    {
        var candidates = LocalUpdateService.ResolveCandidates("asdf");

        Assert.Contains(LocalUpdateService.DefaultFolder, candidates);
        Assert.DoesNotContain("asdf", candidates);
    }

    [Fact]
    public void KotakFolderUpdateDikunciReadOnly()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml");

        var textBox = doc.Descendants()
            .FirstOrDefault(e => (e.Attribute("Name")?.Value ?? e.Attribute(Xaml + "Name")?.Value) == "Part_UpdateFolderPath")
            ?? throw new Xunit.Sdk.XunitException("Kotak folder update (Part_UpdateFolderPath) tidak ditemukan.");

        Assert.Equal("True", textBox.Attribute("IsReadOnly")?.Value);
        Assert.DoesNotContain("TwoWay", textBox.Attribute("Text")?.Value ?? "");
    }

    [Fact]
    public void AdaTombolResetKeDefault()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml");

        var button = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .FirstOrDefault(b => (b.Attribute("Command")?.Value ?? "").Contains("ResetUpdateFolderPathCommand"));

        Assert.True(button is not null, "Settings tidak punya tombol ResetUpdateFolderPathCommand.");
    }

    [Fact]
    public void ViewModelPunyaCommandResetFolderUpdate()
    {
        Assert.NotNull(typeof(SettingsViewModel).GetProperty("ResetUpdateFolderPathCommand"));
    }
}
