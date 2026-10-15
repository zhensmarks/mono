using System.Xml.Linq;
using BMachine.SDK;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Card detail: tombol copy link Trello + buka kartu di browser default,
/// dan reset textbox autocomplete Move/Batch Move setelah panel ditutup.
/// </summary>
public class CardLinkAndMoveSearchResetTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WORKSPACES.md")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mono repository root from the test output directory.");
    }

    private static XDocument ReadAxaml(string relativePath) => XDocument.Parse(ReadRepoFile(relativePath));

    private static XElement FindByName(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(e => e.Attribute(Xaml + "Name")?.Value == name)
        ?? throw new Xunit.Sdk.XunitException($"Elemen x:Name='{name}' tidak ditemukan.");

    [Fact]
    public void DetailPanelHasCopyLinkAndOpenInBrowserButtons()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/CardDetailPanelHost.axaml");

        var buttons = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .ToList();

        var copyButton = buttons.FirstOrDefault(b => (b.Attribute("Command")?.Value ?? "").Contains("CopyCardLinkCommand"));
        var openButton = buttons.FirstOrDefault(b => (b.Attribute("Command")?.Value ?? "").Contains("OpenCardInBrowserCommand"));

        Assert.True(copyButton is not null, "Tidak ada tombol dengan Command CopyCardLinkCommand di panel detail kartu.");
        Assert.True(openButton is not null, "Tidak ada tombol dengan Command OpenCardInBrowserCommand di panel detail kartu.");

        // Keduanya harus mengirim kartu yang sedang dibuka, bukan kartu lain.
        Assert.Contains("SelectedCard", copyButton!.Attribute("CommandParameter")?.Value ?? "");
        Assert.Contains("SelectedCard", openButton!.Attribute("CommandParameter")?.Value ?? "");
    }

    [Theory]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/CardDetailPanelHost.axaml", "Part_MoveBoardBox", "MoveBoardSearchText")]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/CardDetailPanelHost.axaml", "Part_MoveListBox", "MoveListSearchText")]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/UnifiedTrelloView.axaml", "Part_BatchMoveBoardBox", "MoveBoardSearchText")]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/UnifiedTrelloView.axaml", "Part_BatchMoveListBox", "MoveListSearchText")]
    public void MoveSearchBoxesBindTextTwoWay(string file, string boxName, string propertyName)
    {
        var box = FindByName(ReadAxaml(file), boxName);

        var text = box.Attribute("Text")?.Value ?? "";
        Assert.Contains(propertyName, text);
        Assert.Contains("TwoWay", text);
    }

    [Fact]
    public void CloseMovePanelClearsMoveSearchText()
    {
        var vm = new MoveSearchProbe(new FakeDatabase());
        vm.Seed("r", "BANDUNG 2 (RUDI)");

        Assert.Equal("r", vm.MoveBoardSearchText);
        Assert.Equal("BANDUNG 2 (RUDI)", vm.MoveListSearchText);

        vm.Close();

        Assert.Equal(string.Empty, vm.MoveBoardSearchText);
        Assert.Equal(string.Empty, vm.MoveListSearchText);
    }

    public sealed class MoveSearchProbe : EditingCardListViewModel
    {
        public MoveSearchProbe(IDatabase database) : base(database) { }

        public void Seed(string board, string list)
        {
            MoveBoardSearchText = board;
            MoveListSearchText = list;
        }

        public void Close() => CloseMovePanel();
    }
}
