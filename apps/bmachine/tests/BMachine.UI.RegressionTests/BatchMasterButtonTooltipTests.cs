using BMachine.UI.Models;
using Xunit;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Tombol master di footer tab BATCH memakai ikon saja (32x32), jadi tiap tombol WAJIB
/// punya tooltip yang menyebut skrip masternya (DESIGN_SYSTEM.md §24).
/// </summary>
public class BatchMasterButtonTooltipTests
{
    [Fact]
    public void TooltipMentionsScriptFileNameWhenAliasDiffers()
    {
        var option = new BatchScriptOption
        {
            Name = "ONE FOR ALL",
            OriginalName = "buat_master.py",
            Path = @"C:\mono\apps\bmachine\Scripts\Master\buat_master.py"
        };

        Assert.Equal("ONE FOR ALL (buat_master.py)", option.ToolTipText);
    }

    [Fact]
    public void TooltipDoesNotRepeatIdenticalAlias()
    {
        var option = new BatchScriptOption
        {
            Name = "MANASIK",
            OriginalName = "manasik.py",
            Path = @"C:\mono\apps\bmachine\Scripts\Master\manasik.py"
        };

        Assert.Equal("MANASIK", option.ToolTipText);
    }

    [Fact]
    public void TooltipFallsBackToFileNameWithoutAlias()
    {
        var option = new BatchScriptOption
        {
            Name = "",
            OriginalName = "wisuda.py",
            Path = @"C:\mono\apps\bmachine\Scripts\Master\wisuda.py"
        };

        Assert.Equal("wisuda.py", option.ToolTipText);
    }

    [Fact]
    public void MasterButtonInBatchFooterBindsTooltip()
    {
        var view = ReadRepoFile("apps/bmachine/src/BMachine.UI/Views/DashboardView.axaml");

        // Baris tombol master: ikon-only + CommandParameter item -> tooltip dari item itu.
        Assert.Contains("ItemsSource=\"{Binding BatchVM.MasterScriptOrderList}\"", view);
        Assert.Contains("ToolTip.Tip=\"{Binding ToolTipText}\"", view);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "WORKSPACES.md")))
                return System.IO.File.ReadAllText(System.IO.Path.Combine(current.FullName, relativePath));
            current = current.Parent;
        }

        throw new System.IO.FileNotFoundException($"{relativePath} tidak ditemukan dari {AppContext.BaseDirectory}");
    }
}
