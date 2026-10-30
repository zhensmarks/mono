using System.Collections.ObjectModel;
using System.Xml.Linq;
using BMachine.UI.Models;
using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Regresi: panel checklist unified Trello harus mengikuti isi Trello tanpa restart app.
/// Yang diuji adalah lapisan diff-nya (MergeChecklists) — inti dari "checklist baru muncul
/// tanpa Clear + re-add", sehingga panel tidak flicker dan checkbox tidak ke-reset.
/// </summary>
public class ChecklistSyncTests
{
    private static TrelloChecklist Checklist(string id, string name, params (string Id, string Name, bool Done)[] items)
    {
        var checklist = new TrelloChecklist { Id = id, Name = name, IdCard = "card-1" };
        foreach (var item in items)
        {
            checklist.Items.Add(new TrelloChecklistItem { Id = item.Id, Name = item.Name, State = item.Done ? "complete" : "incomplete" });
        }
        return checklist;
    }

    [Fact]
    public void ChecklistBaruDariTrelloMasukTanpaMenghapusYangSudahAda()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "#EDITING USER", ("i-1", "Step 1", true))
        };
        var existingInstance = target[0];

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-1", "#EDITING USER", ("i-1", "Step 1", true)),
            Checklist("cl-2", "#REVISI USER", ("i-2", "Revisi 1", false))
        });

        Assert.Equal(2, target.Count);
        Assert.Same(existingInstance, target[0]);       // instance lama dipertahankan (tidak flicker)
        Assert.Equal("cl-2", target[1].Id);             // checklist baru muncul
        Assert.Equal("#REVISI USER", target[1].Name);
        Assert.Single(target[1].Items);
    }

    [Fact]
    public void ChecklistYangDihapusDiTrelloIkutHilang()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "A"),
            Checklist("cl-2", "B")
        };

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-2", "B")
        });

        Assert.Single(target);
        Assert.Equal("cl-2", target[0].Id);
    }

    [Fact]
    public void ItemBaruDanRenameItemIkutTersinkron()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "A", ("i-1", "Lama", false))
        };
        var item = target[0].Items[0];

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-1", "A", ("i-1", "Baru", true), ("i-2", "Tambahan", false))
        });

        Assert.Equal(2, target[0].Items.Count);
        Assert.Same(item, target[0].Items[0]);
        Assert.Equal("Baru", target[0].Items[0].Name);
        Assert.True(target[0].Items[0].IsChecked);
        Assert.Equal("Tambahan", target[0].Items[1].Name);
        Assert.Equal("1/2", target[0].ProgressText);
    }

    [Fact]
    public void ItemYangDihapusDiTrelloIkutHilangDanProgresDihitungUlang()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "A", ("i-1", "Satu", true), ("i-2", "Dua", true), ("i-3", "Tiga", false))
        };

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-1", "A", ("i-3", "Tiga", false))
        });

        Assert.Single(target[0].Items);
        Assert.Equal("i-3", target[0].Items[0].Id);
        Assert.Equal("0/1", target[0].ProgressText);
        Assert.Equal(1, target[0].TotalCount);
        Assert.Equal(0, target[0].CompletedCount);
    }

    [Fact]
    public void UrutanChecklistMengikutiTrello()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "A"),
            Checklist("cl-2", "B"),
            Checklist("cl-3", "C")
        };

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-3", "C"),
            Checklist("cl-1", "A"),
            Checklist("cl-2", "B")
        });

        Assert.Equal(new[] { "cl-3", "cl-1", "cl-2" }, target.Select(c => c.Id));
    }

    [Fact]
    public void NamaChecklistYangDiRenameIkutBerubah()
    {
        var target = new ObservableCollection<TrelloChecklist>
        {
            Checklist("cl-1", "#EDITING LAMA")
        };

        BaseTrelloListViewModel.MergeChecklists(target, new List<TrelloChecklist>
        {
            Checklist("cl-1", "#EDITING BARU")
        });

        Assert.Equal("#EDITING BARU", target[0].Name);
    }

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

    /// <summary>
    /// Panel checklist harus punya tombol refresh manual, jadi user bisa memaksa tarik ulang
    /// dari Trello kapan saja tanpa menutup panel / restart aplikasi.
    /// </summary>
    [Theory]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/CardDetailPanelHost.axaml")]
    [InlineData("apps/bmachine/src/BMachine.UI/Views/EditingCardListView.axaml")]
    public void PanelChecklistPunyaTombolRefresh(string file)
    {
        var doc = ReadAxaml(file);

        var panel = doc.Descendants()
            .FirstOrDefault(e => e.Attribute(Xaml + "Name")?.Value == "Part_ChecklistPanel")
            ?? throw new Xunit.Sdk.XunitException("Panel checklist (Part_ChecklistPanel) tidak ditemukan.");

        var refreshButton = panel.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .FirstOrDefault(b => (b.Attribute("Command")?.Value ?? "").Contains("RefreshChecklistsCommand"));

        Assert.True(refreshButton is not null, "Panel checklist tidak punya tombol RefreshChecklistsCommand.");
        Assert.Contains("IconRefresh", refreshButton!.ToString());
    }

    /// <summary>Header tiap checklist menampilkan progres supaya perubahan dari Trello kelihatan.</summary>
    [Fact]
    public void HeaderChecklistMenampilkanProgres()
    {
        var doc = ReadAxaml("apps/bmachine/src/BMachine.UI/Views/CardDetailPanelHost.axaml");
        Assert.Contains("ProgressText", doc.ToString());
    }
}
