using System.Xml.Linq;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class LogoViewportRegressionTests
{
    private static readonly string[] OriginalLogoPaths =
    [
        "M12 2L2 7l10 5 10-5-10-5z",
        "M2 12l10 5 10-5",
        "M2 17l10 5 10-5"
    ];

    [Theory]
    [InlineData("SplashWindow.axaml")]
    [InlineData("SettingsView.axaml")]
    public void LogoUsesShared24By24ViewportAndOriginalPathCoordinates(string viewFile)
    {
        var document = XDocument.Load(ViewPath(viewFile));
        var logoViewbox = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Viewbox")
            .Single(element => element.Descendants().Any(path =>
                path.Name.LocalName == "Path" &&
                (string?)path.Attribute("Data") == OriginalLogoPaths[0]));

        var canvas = logoViewbox
            .Elements()
            .Single(element => element.Name.LocalName == "Canvas");

        Assert.Equal("24", (string?)canvas.Attribute("Width"));
        Assert.Equal("24", (string?)canvas.Attribute("Height"));
        Assert.DoesNotContain(logoViewbox.Elements(), element => element.Name.LocalName == "Grid");

        var paths = canvas
            .Elements()
            .Where(element => element.Name.LocalName == "Path")
            .Select(path => (string?)path.Attribute("Data"))
            .ToArray();
        Assert.Equal(OriginalLogoPaths, paths);
    }

    private static string ViewPath(string viewFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "apps",
                "bmachine",
                "src",
                "BMachine.UI",
                "Views",
                viewFile);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate BMachine.UI view '{viewFile}'.");
    }
}
