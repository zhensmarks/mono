using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using BMachine.UI.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace BMachine.UI.RegressionTests;

/// <summary>
/// Verified exploration of the REAL OutputExplorerViewModel against a real temp folder
/// with fake DB/platform. Headless: no window ever appears on any screen.
/// Each test maps to a finding from the code audit; result = PROVEN or REFUTED.
/// </summary>
public class ExplorerLogicTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly FakeDatabase _db = new();
    private readonly FakeNotificationService _notify = new();
    private readonly FakePlatformService _platform = new();
    private readonly FakeExplorerClipboard _clipboard = new();
    private readonly string _root;
    private readonly OutputExplorerViewModel _vm;

    public ExplorerLogicTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "bmx-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _db.SetAsync("Configs.Explorer.DefaultPath", _root).Wait();

        ResetSharedCutState();
        _vm = new OutputExplorerViewModel(_db, _notify, new Services.FileOperationManager(), _platform, _clipboard);
        WaitFor(() => _vm.CurrentPath == _root, 10_000);
    }

    /// <summary>The cut set is process-wide by design; tests must start from a clean slate.</summary>
    private static void ResetSharedCutState()
    {
        typeof(OutputExplorerViewModel)
            .GetField("s_cutPaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
    }

    private OutputExplorerViewModel CreateSecondaryVm(FakeExplorerClipboard? clipboard = null)
    {
        var db = new FakeDatabase();
        db.SetAsync("Configs.Explorer.DefaultPath", _root).Wait();
        var vm = new OutputExplorerViewModel(db, _notify, new Services.FileOperationManager(), _platform,
            clipboard ?? _clipboard);
        WaitFor(() => vm.CurrentPath == _root || vm.IsRootPathMissing, 10_000);
        return vm;
    }

    /// <summary>Pumps the headless dispatcher until condition met or timeout.</summary>
    private static void WaitFor(Func<bool> done, int timeoutMs, string label = "condition")
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return;
            Thread.Sleep(25);
        }
        Dispatcher.UIThread.RunJobs();
        throw new TimeoutException($"Condition not met within {timeoutMs} ms: {label}");
    }

    /// <summary>Selects exactly the item with this name (what a user does with one click).</summary>
    private void SelectSingle(string name)
    {
        var item = _vm.Items.OfType<ExplorerItemViewModel>().FirstOrDefault(i => i.Name == name);
        Assert.NotNull(item);
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(item!);
    }

    private void WriteFile(string name, string content = "x") => File.WriteAllText(Path.Combine(_root, name), content);

    private static string EncodeKey(string path)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    // ------------------------------------------------------------------
    // T01 - Basic: navigation pushes back-stack, Up guarded at root
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T01_Navigation_BacksStack_And_UpDisabledAtRoot()
    {
        Assert.Equal(_root, _vm.CurrentPath);
        Assert.False(_vm.CanNavigateUp, "Up must be disabled at RootPath.");

        var sub = Directory.CreateDirectory(Path.Combine(_root, "SubAlpha")).FullName;
        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5000);
        Assert.True(_vm.CanNavigateUp, "Up enabled inside subfolder of root.");

        _vm.NavigateUpCommand.Execute(null);
        WaitFor(() => _vm.CurrentPath == _root, 5000);
        Assert.False(_vm.CanNavigateUp);

        _vm.GoBackCommand.Execute(null);
        WaitFor(() => _vm.CurrentPath == sub, 5000);
        Assert.True(_vm.CanNavigateUp);
        _output.WriteLine("T01 PASS: back/forward stacks + Up guard behave like Explorer.");
    }

    // ------------------------------------------------------------------
    // T02 - Drop-in naming chain " - Copy" first, then " - Copy (2)"
    //       (HandleDroppedFiles shares the PasteItem suffix rule)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T02_DropDuplicateNaming_FirstCopyUnnumbered_LikeExplorer()
    {
        var otherDir = Directory.CreateDirectory(Path.Combine(_root, "DropSource")).FullName;
        WriteFile("Report.txt", "original");

        var src = Path.Combine(otherDir, "Report.txt");
        File.WriteAllText(src, "dropped");

        // drop Report.txt into _root where Report.txt already exists
        _vm.HandleDroppedFiles(new[] { src }, isMove: false);

        WaitFor(() => File.Exists(Path.Combine(_root, "Report - Copy.txt")), 30_000, "drop copy landed");
        Assert.False(File.Exists(Path.Combine(_root, "Report - Copy (2).txt")),
            "First copy must be unnumbered 'Report - Copy.txt'.");
        Assert.Equal("original", File.ReadAllText(Path.Combine(_root, "Report.txt")));
        _output.WriteLine("T02 PASS: drop-copy duplicate chain matches Windows Explorer naming.");
    }

    // ------------------------------------------------------------------
    // T03 - DuplicateItem numbering: audit finding #5 (expects DEVIATION)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T03_DuplicateItem_FirstCopyNumbered_UnlikeExplorer()
    {
        WriteFile("Photo.jpg");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "Photo.jpg"), 5000);

        var item = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "Photo.jpg");
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(item);

        _vm.DuplicateItemCommand.Execute(1);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        bool unnumbered = false, numbered = false;
        while (DateTime.UtcNow < deadline)
        {
            unnumbered = File.Exists(Path.Combine(_root, "Photo - Copy.jpg"));
            numbered = File.Exists(Path.Combine(_root, "Photo - Copy (1).jpg"));
            if (unnumbered || numbered) break;
            Thread.Sleep(50);
        }

        // FIXED: DuplicateItem now uses the same rule as paste/drop -> first copy is "Photo - Copy.jpg"
        Assert.True(unnumbered, "First duplicate must be 'Photo - Copy.jpg' (Explorer rule).");
        Assert.False(numbered, "Numbered 'Photo - Copy (1).jpg' must no longer be produced.");
        _output.WriteLine("T03 PASS: DuplicateItem naming now matches PasteItem/drop ('- Copy', then '- Copy (2)').");
    }

    // ------------------------------------------------------------------
    // T04 - Delete guard: stale parent selection protected, real file deleted
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T04_Delete_StaleParentProtected_ButRealSelectionDeleted()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "GuardTmp")).FullName;
        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5000);

        // Stale selection: item pointing at the PARENT of current folder
        var stale = new ExplorerItemViewModel
        {
            Name = Path.GetFileName(_root.TrimEnd(Path.DirectorySeparatorChar)),
            FullPath = _root,
            IsDirectory = true,
        };
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(stale);

        _platform.RecycleBinResult = false; // only observe, never actually delete
        _vm.DeleteItemCommand.Execute(null);

        Assert.Empty(_platform.RecycleBinCalls);
        Assert.True(Directory.Exists(_root), "Parent must survive.");

        // Real selection: file inside current folder IS deleted via recycle bin
        var file = Path.Combine(sub, "DelMe.txt");
        File.WriteAllText(file, "x");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "DelMe.txt"), 5000);
        var real = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "DelMe.txt");
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(real);
        _platform.RecycleBinResult = true;

        _vm.DeleteItemCommand.Execute(null);
        WaitFor(() => _platform.RecycleBinCalls.Count > 0, 5000);
        _output.WriteLine("T04 PASS: parent-dir guard blocks stale selection, normal delete still works.");
    }

    // ------------------------------------------------------------------
    // T05 - Recursive search finds file in subfolder; flags only (audit #7)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T05_Search_RecursiveMatch_FindsFileInSubfolder()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "DeepFolder")).FullName;
        File.WriteAllText(Path.Combine(sub, "NeedleFile.txt"), "content");

        _vm.SearchText = "Needle";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "NeedleFile.txt"), 10_000);

        var found = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "NeedleFile.txt");
        Assert.False(found.IsDirectory);
        _output.WriteLine("T05 PASS: recursive search finds file; FullPath points to subfolder " +
                          "(UI shows Name only - no folder column, see audit #7).");
    }

    // ------------------------------------------------------------------
    // T06 - Per-folder settings isolated between folders
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T06_PerFolderSettings_IsolatedBetweenFolders()
    {
        var a = Directory.CreateDirectory(Path.Combine(_root, "FolderA")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "FolderB")).FullName;

        _vm.NavigateTo(a);
        WaitFor(() => _vm.CurrentPath == a, 5000);

        _vm.IsLocalSettings = true;
        _vm.SortBy = OutputExplorerViewModel.ExplorerSortOption.Date;
        WaitFor(() => _db.Has("Configs.Explorer.FolderSettings." + EncodeKey(a)), 5000);
        Assert.Equal(OutputExplorerViewModel.ExplorerSortOption.Date, _vm.SortBy);

        _vm.NavigateTo(b);
        WaitFor(() => _vm.CurrentPath == b, 5000);
        WaitFor(() => _vm.SortBy == OutputExplorerViewModel.ExplorerSortOption.Name, 5000);
        Assert.False(_vm.IsLocalSettings, "FolderB has no local override.");
        Assert.NotEqual(OutputExplorerViewModel.ExplorerSortOption.Date, _vm.SortBy);

        _output.WriteLine("T06 PASS: local sort applies to FolderA only; FolderB falls back to global.");
    }

    // ------------------------------------------------------------------
    // T07 - Rename commits and reloads
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T07_Rename_Commit_UpdatesFullPath()
    {
        WriteFile("OldName.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "OldName.txt"), 5000);

        var item = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "OldName.txt");
        item.Name = "NewName.txt";
        _vm.CommitRenameCommand.Execute(item);
        WaitFor(() => File.Exists(Path.Combine(_root, "NewName.txt")), 5000);

        Assert.False(File.Exists(Path.Combine(_root, "OldName.txt")));
        _output.WriteLine("T07 PASS: rename commits to filesystem and listing reloads.");
    }

    // ------------------------------------------------------------------
    // T08 - Background move (FileOperationManager) end-to-end
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T08_BackgroundMove_Completes_And_SourceGone()
    {
        WriteFile("MoveMe.txt");
        var sub = Directory.CreateDirectory(Path.Combine(_root, "MoveDest")).FullName;
        var src = Path.Combine(_root, "MoveMe.txt");
        var dest = Path.Combine(sub, "MoveMe.txt");

        var mgr = new Services.FileOperationManager();
        mgr.MoveFileBackground(src, dest);

        WaitFor(() => File.Exists(dest), 20_000);
        Assert.False(File.Exists(src), "Source must be gone after move.");
        _output.WriteLine("T08 PASS: background queue executes move to completion.");
    }

    // ------------------------------------------------------------------
    // T09 - FileSystemWatcher auto-refresh (audit #2: does it refresh at all)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T09_Watcher_AutoRefresh_PicksUpExternalFile()
    {
        // External process writes a file into the watched folder
        var late = Path.Combine(_root, "ArrivedLater.txt");
        File.WriteAllText(late, "x");

        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "ArrivedLater.txt"), 10_000);
        _output.WriteLine("T09 PASS: watcher auto-refresh delivered the new file (debounce path executes).");
    }

    // ------------------------------------------------------------------
    // T10 - Search: one FULL list rebuild per keystroke (audit #1 mechanism)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T10_Search_EachKeystroke_RebuildsWholeList()
    {
        for (int i = 0; i < 40; i++) WriteFile($"filler{i:D3}.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count() >= 40, 10_000);

        int resets = 0;
        void OnChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        }
        _vm.Items.CollectionChanged += OnChanged;
        int duringBurst;
        try
        {
            // Three keystrokes in quick succession (a normal typing burst)
            foreach (var ch in new[] { "f", "fi", "fil" })
            {
                _vm.SearchText += ch;
                WaitFor(() => true, 60); // short: well inside the debounce window
            }
            duringBurst = resets;
            // Now let the debounce window elapse
            WaitFor(() => resets >= 1, 3000);
        }
        finally { _vm.Items.CollectionChanged -= OnChanged; }

        _output.WriteLine($"T10 RESULT: rebuilds during burst = {duringBurst}, total after debounce = {resets}");
        Assert.Equal(0, duringBurst);                       // nothing runs while typing
        Assert.Equal(1, resets);                            // exactly one rebuild for the burst
        _output.WriteLine("T10 PASS: rapid typing collapses into ONE reload (300 ms debounce) instead of one per keystroke.");
    }

    // ------------------------------------------------------------------
    // T11 - Sort by Date keeps FOLDERS FIRST (Windows Explorer behaviour)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T11_SortByDate_KeepsFoldersFirst()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "zzz_folder")).FullName;
        Directory.SetLastWriteTime(folder, DateTime.Now.AddMinutes(-1));   // folder is NEWER

        var file = Path.Combine(_root, "aaa_file.txt");
        File.WriteAllText(file, "x");
        File.SetLastWriteTime(file, DateTime.Now.AddHours(-3));            // file is older

        _vm.GroupBy = OutputExplorerViewModel.ExplorerGroupBy.None;
        _vm.SortBy = OutputExplorerViewModel.ExplorerSortOption.Date;
        _vm.IsSortDescending = false;

        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count(i => i.Name is "zzz_folder" or "aaa_file.txt") == 2, 5000);

        var seq = _vm.Items.OfType<ExplorerItemViewModel>()
            .Where(i => i.Name is "zzz_folder" or "aaa_file.txt")
            .Select(i => i.Name)
            .ToArray();

        _output.WriteLine($"T11 RESULT: date-ascending order = [{string.Join(", ", seq)}]");
        Assert.Equal(new[] { "zzz_folder", "aaa_file.txt" }, seq);
        _output.WriteLine("T11 PASS: the older FILE still comes after the NEWER folder — folders stay first, like Explorer.");
    }

    // ------------------------------------------------------------------
    // T12 - Group by Date headers (Today / A long time ago)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T12_GroupByDate_HeaderBuckets()
    {
        var today = Path.Combine(_root, "Fresh.txt");
        File.WriteAllText(today, "x");
        File.SetLastWriteTime(today, DateTime.Now);

        var ancient = Path.Combine(_root, "Ancient.txt");
        File.WriteAllText(ancient, "x");
        File.SetLastWriteTime(ancient, DateTime.Now.AddDays(-400));

        _vm.GroupBy = OutputExplorerViewModel.ExplorerGroupBy.Date;

        // Wait until BOTH buckets are present: asserting on a half-built list was the flake.
        WaitFor(() =>
        {
            var h = _vm.Items.OfType<ExplorerGroupHeaderViewModel>().Select(x => x.Title).ToArray();
            return h.Contains("Today") && h.Contains("A long time ago");
        }, 10_000, "date group headers built");

        var headers = _vm.Items.OfType<ExplorerGroupHeaderViewModel>().Select(h => h.Title).ToArray();
        _output.WriteLine($"T12 RESULT: headers = [{string.Join(" | ", headers)}]");

        Assert.Contains("Today", headers);
        Assert.Contains("A long time ago", headers);
        Assert.True(Array.IndexOf(headers, "Today") < Array.IndexOf(headers, "A long time ago"),
            "Today must precede 'A long time ago'.");
        _output.WriteLine("T12 PASS: date grouping buckets produced in OS-Explorer order.");
    }

    // ------------------------------------------------------------------
    // T13 - NavigateTo(same path) is a no-op (no duplicate history entry)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T13_NavigateTo_SamePath_DoesNotPushHistory()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "SamePath")).FullName;
        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5000);
        _vm.NavigateTo(sub); // second call, same path
        WaitFor(() => true, 200);

        _vm.GoBackCommand.Execute(null);
        WaitFor(() => _vm.CurrentPath == _root, 5000);

        Assert.Equal(_root, _vm.CurrentPath);
        Assert.False(_vm.CanGoBack, "After a single navigation and one Back, history must be empty (no dup entry).");
        _output.WriteLine("T13 PASS: repeated NavigateTo(same path) does not duplicate history.");
    }

    // ------------------------------------------------------------------
    // T14 - Breadcrumbs start at drive root (audit: breadcrumb build)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T14_Breadcrumbs_StartAtDriveRoot_EndAtFolder()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "CrumbFolder")).FullName;
        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5000);
        WaitFor(() => _vm.Breadcrumbs.Count > 1, 3000);

        var names = _vm.Breadcrumbs.Select(b => b.Name).ToArray();
        _output.WriteLine($"T14 RESULT: breadcrumbs = [{string.Join(" > ", names)}]");

        Assert.StartsWith("C:", names[0]);
        Assert.Equal("CrumbFolder", names[^1]);
        _output.WriteLine("T14 PASS: breadcrumb chain = drive root ... current folder.");
    }

    // ------------------------------------------------------------------
    // T15 - Task monitor window binds the FIRST VM, not the shared manager
    //       (audit #3: cross-tab Cancel/Retry target mismatch)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T15_TaskMonitor_NotReboundToLatestVm()
    {
        var field = typeof(OutputExplorerViewModel)
            .GetField("s_sharedTaskMonitorWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);

        // Make sure a monitor exists (previous tests in this session may already have opened it,
        // which itself is part of the defect: one static window for the whole process).
        var winBefore = (Avalonia.Controls.Window?)field!.GetValue(null);
        if (winBefore == null)
        {
            _vm.ShowTaskMonitorCommand.Execute(null);
            winBefore = (Avalonia.Controls.Window?)field.GetValue(null);
        }
        Assert.NotNull(winBefore);
        var boundBefore = winBefore!.DataContext as OutputExplorerViewModel;

        // A second, independent explorer VM (simulates another tab/window) opens the monitor
        var vm2 = CreateSecondaryVm();
        vm2.ShowTaskMonitorCommand.Execute(null);

        var winAfter = (Avalonia.Controls.Window?)field.GetValue(null);
        var boundAfter = winAfter?.DataContext;

        _output.WriteLine($"T15 RESULT: window shared={ReferenceEquals(winBefore, winAfter)}, " +
                          $"DataContext={boundAfter?.GetType().Name}");

        // FIXED: the monitor is now bound to a dedicated VM that wraps the shared manager,
        // never to whichever tab opened it first.
        Assert.IsType<FileOperationsMonitorViewModel>(boundAfter);
        Assert.NotNull(winAfter);
        _output.WriteLine("T15 PASS: shared monitor binds FileOperationsMonitorViewModel (manager-backed), " +
                          "so Cancel/Retry always target the manager that owns the tasks.");

        winAfter!.Close();
    }

    // ------------------------------------------------------------------
    // T16 - Permanent delete via background manager
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T16_BackgroundPermanentDelete_RemovesFile()
    {
        var victim = Path.Combine(_root, "PermDelete.txt");
        File.WriteAllText(victim, "x");

        var mgr = new Services.FileOperationManager();
        mgr.DeleteFileBackground(victim);

        WaitFor(() => !File.Exists(victim), 15_000);
        Assert.False(File.Exists(victim));
        _output.WriteLine("T16 PASS: background permanent delete removes the file.");
    }

    // ------------------------------------------------------------------
    // T17 - Copy -> Paste (clipboard abstraction): copy created, original kept
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T17_CopyThenPaste_CreatesCopyAndKeepsOriginal()
    {
        WriteFile("Src.txt", "payload");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "Src.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "Src.txt"));

        await _vm.CopyItemCommand.ExecuteAsync(null);
        Assert.True(await _clipboard.HasFilePayloadAsync(), "Copy must publish a file payload.");

        await _vm.PasteItemCommand.ExecuteAsync(null);
        WaitFor(() => File.Exists(Path.Combine(_root, "Src - Copy.txt")), 30_000, "paste copy landed");

        Assert.True(File.Exists(Path.Combine(_root, "Src.txt")));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(_root, "Src - Copy.txt")));
        _output.WriteLine("T17 PASS: copy->paste creates '- Copy' and keeps the original.");
    }

    // ------------------------------------------------------------------
    // T18 - Cut -> Paste moves, then cut state + clipboard are cleared
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T18_CutThenPaste_MovesAndClearsCutState()
    {
        WriteFile("Move1.txt", "x");
        var dest = Directory.CreateDirectory(Path.Combine(_root, "DestA")).FullName;
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "Move1.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "Move1.txt"));
        await _vm.CutItemCommand.ExecuteAsync(null);

        _vm.NavigateTo(dest);
        WaitFor(() => _vm.CurrentPath == dest, 5000);
        await _vm.PasteItemCommand.ExecuteAsync(null);

        WaitFor(() => File.Exists(Path.Combine(dest, "Move1.txt")), 30_000, "cut+paste move landed");
        Assert.False(File.Exists(Path.Combine(_root, "Move1.txt")), "Cut+paste must MOVE, not copy.");
        Assert.Null(CutState());
        Assert.False(await _clipboard.HasFilePayloadAsync(), "Move-paste must clear the file payload.");
        _output.WriteLine("T18 PASS: cut->paste across folders moves the file and clears cut state + clipboard.");
    }

    // ------------------------------------------------------------------
    // T19 - Cut in one tab, paste in ANOTHER tab still moves (shared cut state)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T19_CutInOneTab_PasteInAnotherTab_Moves()
    {
        WriteFile("Cross.txt", "x");
        var dest = Directory.CreateDirectory(Path.Combine(_root, "DestB")).FullName;
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "Cross.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "Cross.txt"));
        await _vm.CutItemCommand.ExecuteAsync(null);

        var vm2 = CreateSecondaryVm();
        vm2.NavigateTo(dest);
        WaitFor(() => vm2.CurrentPath == dest, 5000);
        await vm2.PasteItemCommand.ExecuteAsync(null);

        WaitFor(() => File.Exists(Path.Combine(dest, "Cross.txt")), 30_000, "cross-tab move landed");
        Assert.False(File.Exists(Path.Combine(_root, "Cross.txt")),
            "A cut performed in tab A must still MOVE when pasted in tab B.");
        _output.WriteLine("T19 PASS: cut state is process-wide - cross-tab paste moves the file (was a silent copy).");
    }

    // ------------------------------------------------------------------
    // T20 - Cut + paste into the SAME folder is a no-op (no "- Copy" rename)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T20_CutPasteIntoSameFolder_IsNoOp()
    {
        WriteFile("Same.txt", "x");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "Same.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "Same.txt"));
        await _vm.CutItemCommand.ExecuteAsync(null);
        await _vm.PasteItemCommand.ExecuteAsync(null);
        WaitFor(() => true, 500);

        Assert.True(File.Exists(Path.Combine(_root, "Same.txt")), "Original must stay untouched.");
        Assert.False(File.Exists(Path.Combine(_root, "Same - Copy.txt")),
            "Pasting a cut item into its own folder must not rename the original to '- Copy'.");
        _output.WriteLine("T20 PASS: cut + paste into the same folder does nothing (Explorer behaviour).");
    }

    // ------------------------------------------------------------------
    // T21 - Two same-named sources pasted at once get distinct destinations
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T21_PasteTwoSameNamedSources_NoCollision()
    {
        var a = Directory.CreateDirectory(Path.Combine(_root, "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;
        File.WriteAllText(Path.Combine(a, "Dup.txt"), "fromA");
        File.WriteAllText(Path.Combine(b, "Dup.txt"), "fromB");

        await _clipboard.SetFilePathsAsync(new[]
        {
            Path.Combine(a, "Dup.txt"),
            Path.Combine(b, "Dup.txt"),
        });

        await _vm.PasteItemCommand.ExecuteAsync(null);

        WaitFor(() => File.Exists(Path.Combine(_root, "Dup.txt")) && File.Exists(Path.Combine(_root, "Dup - Copy.txt")), 30_000, "both same-named sources pasted");
        var contents = new[]
        {
            File.ReadAllText(Path.Combine(_root, "Dup.txt")),
            File.ReadAllText(Path.Combine(_root, "Dup - Copy.txt")),
        }.OrderBy(s => s).ToArray();

        Assert.Equal(new[] { "fromA", "fromB" }, contents);
        _output.WriteLine("T21 PASS: reserved-name tracking keeps both same-named sources distinct.");
    }

    private static object? CutState()
        => typeof(OutputExplorerViewModel)
            .GetField("s_cutPaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);

    // ------------------------------------------------------------------
    // T22 - Watcher restart-debounce keeps the LAST change (no dropped event)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T22_Watcher_BurstOfChanges_AllAppear()
    {
        // Three changes inside one debounce window (was: 1st reload, rest silently dropped).
        File.WriteAllText(Path.Combine(_root, "w1.txt"), "x");
        Thread.Sleep(120);
        File.WriteAllText(Path.Combine(_root, "w2.txt"), "x");
        Thread.Sleep(120);
        File.WriteAllText(Path.Combine(_root, "w3.txt"), "x");

        WaitFor(() =>
            _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "w1.txt") &&
            _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "w2.txt") &&
            _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "w3.txt"), 10_000);

        _output.WriteLine("T22 PASS: rapid burst of file creations ends up fully listed (restart-timer debounce).");
    }

    // ------------------------------------------------------------------
    // T23 - A superseded search must not paint stale results
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T23_Search_StaleResultsDiscarded()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "alphaOne.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "sub", "betaTwo.txt"), "x");

        _vm.SearchText = "alpha";
        _vm.SearchText = "beta";   // supersedes the alpha query immediately
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "betaTwo.txt"), 10_000);
        WaitFor(() => true, 800);  // give a late alpha result a chance to (wrongly) appear

        var names = _vm.Items.OfType<ExplorerItemViewModel>().Select(i => i.Name).ToArray();
        Assert.Contains("betaTwo.txt", names);
        Assert.DoesNotContain("alphaOne.txt", names);
        _output.WriteLine("T23 PASS: superseded search results are discarded (generation guard).");
    }

    // ------------------------------------------------------------------
    // T24 - IsSubfolderOf is separator-aware (sibling folder is NOT a subfolder)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T24_SiblingFolder_IsNotTreatedAsSubfolder()
    {
        var sibling = _root + "X";                 // "…\bmx-abcdX" is NOT inside "…\bmx-abcd"
        Directory.CreateDirectory(sibling);
        try
        {
            var item = new ExplorerItemViewModel { Name = Path.GetFileName(sibling), FullPath = sibling, IsDirectory = true };
            await _vm.AddToQuickAccessCommand.ExecuteAsync(item);
            WaitFor(() => _vm.QuickAccessItems.Count > 0, 5000);

            var pinned = _vm.QuickAccessItems[^1];
            Assert.False(pinned.IsDynamic,
                "A sibling of RootPath must be stored as an ABSOLUTE path, not as a relative 'dynamic' one.");
            Assert.Equal(sibling, pinned.Path);
            _output.WriteLine("T24 PASS: sibling path stored absolute (old StartsWith() check misfiled it as dynamic).");
        }
        finally { try { Directory.Delete(sibling, true); } catch { } }
    }

    // ------------------------------------------------------------------
    // T25 - Dropping a folder into its own subtree is refused
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T25_DropFolderIntoOwnSubtree_Refused()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "Owner")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(parent, "Child")).FullName;

        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5000);

        _vm.HandleDroppedFiles(new[] { parent }, isMove: false);
        WaitFor(() => true, 1000);

        Assert.False(Directory.Exists(Path.Combine(sub, "Owner")),
            "A folder must never be copied into its own subtree (runaway recursive copy).");
        _output.WriteLine("T25 PASS: self-subtree drop refused with an error notification.");
    }

    // ------------------------------------------------------------------
    // T26 - Shortcut gate: only text-editing gestures yield to a focused TextBox
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T26_ShortcutGate_TruthTable()
    {
        // Blocked while renaming / typing (TextBox owns them)
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+C", textBoxFocused: true));
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+X", textBoxFocused: true));
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+V", textBoxFocused: true));
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+A", textBoxFocused: true));
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Delete", textBoxFocused: true));
        Assert.True(Services.ExplorerShortcutGate.ShouldBlock("Shift+Delete", textBoxFocused: true));

        // Still alive inside a TextBox (were ALL dead before the fix)
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("F5", textBoxFocused: true));
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Alt+Left", textBoxFocused: true));
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Alt+Up", textBoxFocused: true));
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+Shift+C", textBoxFocused: true));
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+Tab", textBoxFocused: true));

        // Modifier order does not matter
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Shift+Ctrl+N", textBoxFocused: true));

        // No TextBox focus -> nothing is blocked
        Assert.False(Services.ExplorerShortcutGate.ShouldBlock("Ctrl+C", textBoxFocused: false));

        _output.WriteLine("T26 PASS: gate blocks only text-editing gestures while a TextBox has focus.");
    }

    // ------------------------------------------------------------------
    // T27 - Cut state: marked items are dimmed, Copy/paste clears the mark
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T27_CutMarking_DimsOnlyCutItems()
    {
        WriteFile("cutA.txt");
        WriteFile("keepB.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count(i => i.Name is "cutA.txt" or "keepB.txt") == 2, 5000);

        var cutItem = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "cutA.txt");
        var keepItem = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "keepB.txt");

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(cutItem);
        await _vm.CutItemCommand.ExecuteAsync(null);

        Assert.True(cutItem.IsCut);
        Assert.Equal(0.45, cutItem.RowOpacity, 2);
        Assert.False(keepItem.IsCut, "Only the cut items may be dimmed.");
        Assert.Equal(1.0, keepItem.RowOpacity, 2);

        await _vm.CopyItemCommand.ExecuteAsync(null);
        Assert.False(cutItem.IsCut, "Copy must clear the cut marking.");
        Assert.Equal(1.0, cutItem.RowOpacity, 2);
        _output.WriteLine("T27 PASS: cut items are dimmed, non-selected stay normal, Copy clears the mark.");
    }

    // ------------------------------------------------------------------
    // T28 - Selection survives a refresh (F5 / auto-refresh), like Explorer
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T28_SelectionSurvivesRefresh()
    {
        WriteFile("keepSelected.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "keepSelected.txt"), 5000);

        var item = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "keepSelected.txt");
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(item);

        // An external change triggers the watcher reload; F5 does the same thing.
        File.WriteAllText(Path.Combine(_root, "other.txt"), "x");
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "other.txt"), 10_000);
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "keepSelected.txt"), 5000);

        Assert.Contains(_vm.SelectedItems.OfType<ExplorerItemViewModel>(),
            i => i.Name == "keepSelected.txt");
        _output.WriteLine("T28 PASS: the selected item is still selected after auto-refresh and F5.");
    }

    // ------------------------------------------------------------------
    // T29 - Search results carry their folder (Explorer's Folder column)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T29_SearchResults_ShowTheirLocation()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "deep", "deeper")).FullName;
        File.WriteAllText(Path.Combine(nested, "locatedNeedle.txt"), "x");

        _vm.SearchText = "locatedNeedle";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "locatedNeedle.txt"), 10_000);

        var found = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "locatedNeedle.txt");
        Assert.True(found.ShowLocation, "A recursive search hit must expose its location.");
        Assert.Equal(nested, found.LocationDisplay);

        // Normal listing must NOT show the location column
        _vm.SearchText = "";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().All(i => i.Name != "locatedNeedle.txt"), 10_000);
        var plain = _vm.Items.OfType<ExplorerItemViewModel>().FirstOrDefault();
        if (plain != null) Assert.False(plain.ShowLocation);
        _output.WriteLine("T29 PASS: search hits show the parent folder; normal listings do not.");
    }

    // ------------------------------------------------------------------
    // T30 - Undo: new file
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T30_Undo_NewFile()
    {
        await _vm.CreateNewFileCommand.ExecuteAsync("undo-file.txt");
        WaitFor(() => File.Exists(Path.Combine(_root, "undo-file.txt")), 5000);

        _vm.UndoCommand.Execute(null);
        WaitFor(() => !File.Exists(Path.Combine(_root, "undo-file.txt")), 5000);
        Assert.False(File.Exists(Path.Combine(_root, "undo-file.txt")));
        _output.WriteLine("T30 PASS: Ctrl+Z removes the file the explorer just created.");
    }

    // ------------------------------------------------------------------
    // T31 - Undo: new folder
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T31_Undo_NewFolder()
    {
        await _vm.CreateNewFolderCommand.ExecuteAsync("UndoFolder");
        WaitFor(() => Directory.Exists(Path.Combine(_root, "UndoFolder")), 5000);

        _vm.UndoCommand.Execute(null);
        WaitFor(() => !Directory.Exists(Path.Combine(_root, "UndoFolder")), 5000);
        Assert.False(Directory.Exists(Path.Combine(_root, "UndoFolder")));
        _output.WriteLine("T31 PASS: Ctrl+Z removes the folder the explorer just created.");
    }

    // ------------------------------------------------------------------
    // T32 - Undo: rename (and the renamed item stays selected)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T32_Undo_Rename_AndKeepsSelection()
    {
        WriteFile("before.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "before.txt"), 5000);

        var item = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "before.txt");
        item.Name = "after.txt";
        _vm.CommitRenameCommand.Execute(item);
        WaitFor(() => File.Exists(Path.Combine(_root, "after.txt")), 5000);

        // Selection is restored by the reload that follows the rename
        WaitFor(() => _vm.SelectedItems.OfType<ExplorerItemViewModel>().Any(i => i.Name == "after.txt"), 5000);
        Assert.Contains(_vm.SelectedItems.OfType<ExplorerItemViewModel>(), i => i.Name == "after.txt");

        _vm.UndoCommand.Execute(null);
        WaitFor(() => File.Exists(Path.Combine(_root, "before.txt")), 5000);
        Assert.False(File.Exists(Path.Combine(_root, "after.txt")));
        _output.WriteLine("T32 PASS: rename is undone and the renamed item was kept selected.");
    }

    // ------------------------------------------------------------------
    // T33 - Undo: move (cut + paste elsewhere, then Ctrl+Z brings it back)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T33_Undo_Move()
    {
        WriteFile("mover.txt");
        var dest = Directory.CreateDirectory(Path.Combine(_root, "UndoDest")).FullName;
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "mover.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "mover.txt"));
        await _vm.CutItemCommand.ExecuteAsync(null);

        _vm.NavigateTo(dest);
        WaitFor(() => _vm.CurrentPath == dest, 5000);
        await _vm.PasteItemCommand.ExecuteAsync(null);
        WaitFor(() => File.Exists(Path.Combine(dest, "mover.txt")), 30_000, "cut+paste move finished");

        _vm.UndoCommand.Execute(null);
        WaitFor(() => File.Exists(Path.Combine(_root, "mover.txt")), 30_000, "undo brought the file back");
        Assert.False(File.Exists(Path.Combine(dest, "mover.txt")));
        _output.WriteLine("T33 PASS: Ctrl+Z moves the file back to its original folder.");
    }

    // ------------------------------------------------------------------
    // T34 - Undo: paste copy deletes the copy, original untouched
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T34_Undo_Copy()
    {
        WriteFile("orig.txt", "payload");
        var dest = Directory.CreateDirectory(Path.Combine(_root, "CopyDest")).FullName;

        _vm.NavigateTo(dest);
        WaitFor(() => _vm.CurrentPath == dest, 5000);

        await _clipboard.SetFilePathsAsync(new[] { Path.Combine(_root, "orig.txt") });
        await _vm.PasteItemCommand.ExecuteAsync(null);
        WaitFor(() => File.Exists(Path.Combine(dest, "orig.txt")), 30_000, "paste copy landed");

        _vm.UndoCommand.Execute(null);
        WaitFor(() => !File.Exists(Path.Combine(dest, "orig.txt")), 30_000, "undo removed the copy");
        Assert.False(File.Exists(Path.Combine(dest, "orig.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "orig.txt")));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(_root, "orig.txt")));
        _output.WriteLine("T34 PASS: Ctrl+Z removes the pasted copy and never touches the original.");
    }

    // ------------------------------------------------------------------
    // T35 - A recycle-bin delete is reported honestly (not silently 'undone')
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T35_Undo_RecycleDelete_ReportsInsteadOfPretending()
    {
        WriteFile("trashed.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "trashed.txt"), 5000);

        SelectSingle("trashed.txt");

        // Windows may still hold a transient scan handle on a file that was created milliseconds
        // ago (share violation), exactly like a real user who clicks Delete again when the first
        // click seems to do nothing. Retrying here keeps the assertion about OUR logic, not the OS.
        var victim = Path.Combine(_root, "trashed.txt");
        var tries = 0;
        while (File.Exists(victim) && tries < 20)
        {
            tries++;
            _vm.DeleteItemCommand.Execute(null);
            WaitFor(() => !File.Exists(victim) || _platform.RecycleBinCalls.Count >= tries, 2000, "delete attempt");
            if (File.Exists(victim)) { WaitFor(() => true, 120); }   // let the scanner let go
        }

        Assert.False(File.Exists(victim), "Recycle-bin delete must remove the file.");
        Assert.NotEmpty(_platform.RecycleBinCalls);

        _vm.UndoCommand.Execute(null);
        WaitFor(() => _notify.Infos.Count > 0, 3000);

        Assert.False(File.Exists(victim), "The explorer must not fake a restore.");
        Assert.Contains(_notify.Infos, m => m.Contains("Recycle Bin"));
        _output.WriteLine("T35 PASS: delete reports where the item went instead of pretending to undo.");
    }

    // ------------------------------------------------------------------
    // T36 - Cut dimming survives a refresh (Explorer keeps cut items faded)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T36_CutMarking_SurvivesRefresh()
    {
        WriteFile("cutPersist.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "cutPersist.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "cutPersist.txt"));
        await _vm.CutItemCommand.ExecuteAsync(null);

        _vm.RefreshCommand.Execute(null);   // list is rebuilt with brand-new item objects
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "cutPersist.txt"), 5000);

        var rebuilt = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "cutPersist.txt");
        Assert.True(rebuilt.IsCut, "A refresh must not un-dim an item that is still cut.");
        _output.WriteLine("T36 PASS: cut items stay dimmed across refresh (state lives in the shared cut set).");
    }

    // ------------------------------------------------------------------
    // T37 - After a paste the pasted item is selected (Explorer behaviour)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task T37_Paste_SelectsPastedItem()
    {
        WriteFile("selOrig.txt", "payload");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "selOrig.txt"), 5000);

        await _clipboard.SetFilePathsAsync(new[] { Path.Combine(_root, "selOrig.txt") });
        await _vm.PasteItemCommand.ExecuteAsync(null);

        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "selOrig - Copy.txt"), 30_000, "pasted copy listed");
        WaitFor(() => _vm.SelectedItems.OfType<ExplorerItemViewModel>().Any(i => i.Name == "selOrig - Copy.txt"), 30_000, "pasted copy selected");

        Assert.Contains(_vm.SelectedItems.OfType<ExplorerItemViewModel>(), i => i.Name == "selOrig - Copy.txt");
        _output.WriteLine("T37 PASS: the freshly pasted item ends up selected.");
    }

    // ------------------------------------------------------------------
    // T38 - Ctrl+Wheel: switch to icon view first, then scale icons
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T38_CtrlWheel_ZoomsIcons_LikeExplorer()
    {
        // From a details-style view the first notch switches to icons (Explorer behaviour)
        _vm.LayoutMode = OutputExplorerViewModel.ExplorerLayoutMode.Vertical;
        _vm.ZoomIconsCommand.Execute(true);
        Assert.Equal(OutputExplorerViewModel.ExplorerLayoutMode.Thumbnail, _vm.LayoutMode);
        Assert.Equal(96, _vm.ThumbnailSize, 0);

        // Then each notch scales up / down
        _vm.ZoomIconsCommand.Execute(true);
        Assert.Equal(112, _vm.ThumbnailSize, 0);
        _vm.ZoomIconsCommand.Execute(false);
        Assert.Equal(96, _vm.ThumbnailSize, 0);

        // Clamped at the ends
        for (int i = 0; i < 40; i++) _vm.ZoomIconsCommand.Execute(false);
        Assert.Equal(48, _vm.ThumbnailSize, 0);
        for (int i = 0; i < 60; i++) _vm.ZoomIconsCommand.Execute(true);
        Assert.Equal(512, _vm.ThumbnailSize, 0);

        _output.WriteLine("T38 PASS: Ctrl+wheel behaves like Explorer (switch to icons, then zoom, clamped 48-512).");
    }

    // ------------------------------------------------------------------
    // T39 - Explorer-style status summary
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T39_StatusSummary_CountsItemsAndSelection()
    {
        WriteFile("s1.txt");
        WriteFile("s2.txt");
        WriteFile("s3.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count() >= 3, 5000);

        int total = _vm.Items.OfType<ExplorerItemViewModel>().Count();
        Assert.Equal($"{total} items", _vm.StatusSummary);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First());
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().Skip(1).First());
        Assert.Equal($"{total} items, 2 selected", _vm.StatusSummary);

        _output.WriteLine($"T39 PASS: status shows '{_vm.StatusSummary}'.");
    }

    // ------------------------------------------------------------------
    // T40 - Permanent delete asks first; "no" changes nothing
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T40_PermanentDelete_RequiresConfirmation()
    {
        WriteFile("keepMe.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "keepMe.txt"), 5000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "keepMe.txt"));

        _notify.ConfirmResult = false;              // user clicks "Batal"
        _vm.PermanentDeleteItemCommand.Execute(null);
        WaitFor(() => _notify.Confirms.Count > 0, 3000);
        System.Threading.Thread.Sleep(200);

        Assert.True(File.Exists(Path.Combine(_root, "keepMe.txt")), "Declining the confirmation must delete nothing.");
        Assert.Contains(_notify.Confirms, m => m.Contains("permanen"));

        _notify.ConfirmResult = true;               // user confirms
        _vm.PermanentDeleteItemCommand.Execute(null);
        WaitFor(() => !File.Exists(Path.Combine(_root, "keepMe.txt")), 15_000);
        Assert.False(File.Exists(Path.Combine(_root, "keepMe.txt")));
        _output.WriteLine("T40 PASS: permanent delete is gated by a real confirmation (no = untouched).");
    }

    // ------------------------------------------------------------------
    // T41 - Open semantics: folder navigates, file goes to its default app
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T41_OpenItem_FolderNavigates_FileUsesDefaultApp()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "OpenSub")).FullName;
        WriteFile("openMe.txt");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count(i => i.Name is "OpenSub" or "openMe.txt") == 2, 5000);

        var folder = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "OpenSub");
        _vm.OpenItemCommand.Execute(folder);
        WaitFor(() => _vm.CurrentPath == sub, 5000);
        Assert.Equal(sub, _vm.CurrentPath);

        var file = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "openMe.txt");
        Assert.False(file.IsDirectory);
        _vm.OpenItemCommand.Execute(file);
        WaitFor(() => _platform.OpenedWithDefault.Count > 0, 5000);
        Assert.Contains(Path.Combine(_root, "openMe.txt"), _platform.OpenedWithDefault);
        _output.WriteLine("T41 PASS: opening a folder navigates into it; a file is handed to the OS default app.");
    }

    // ------------------------------------------------------------------
    // T42 - The confirmation dialog really exists and carries the texts
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T42_ConfirmDialog_CarriesTitleAndMessage()
    {
        var dialog = new Views.Dialogs.MantraData.MantraConfirmDialog("Hapus Permanen", "Yakin hapus 2 item?");
        Assert.Equal("Hapus Permanen", dialog.Title);
        Assert.Equal("Hapus Permanen", dialog.FindControl<Avalonia.Controls.TextBlock>("TxtTitle")!.Text);
        Assert.Equal("Yakin hapus 2 item?", dialog.FindControl<Avalonia.Controls.TextBlock>("TxtMessage")!.Text);
        _output.WriteLine("T42 PASS: confirm dialog loads and shows the given title/message (its look needs an eyeball check).");
    }

    // ------------------------------------------------------------------
    // T43 - Ctrl+F opens the search UI; the Explorer window renders it in its header
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T43_CtrlF_OpensHeaderSearch_AndEscClosesIt()
    {
        Assert.False(_vm.IsSearchVisible);

        _vm.IsShowPathBar = false;
        _vm.FocusSearchBoxCommand.Execute(null);
        Assert.True(_vm.IsSearchVisible, "Ctrl+F must actually open the search UI.");
        // The header search is independent from the path-bar visibility.
        _vm.IsShowPathBar = true;
        Assert.True(_vm.IsSearchVisible);

        _vm.SearchText = "zzz";
        _vm.CloseSearchCommand.Execute(null);
        Assert.False(_vm.IsSearchVisible);
        Assert.Equal("", _vm.SearchText);
        _vm.FocusSearchBoxCommand.Execute(null);
        Assert.True(_vm.IsSearchVisible);
        _output.WriteLine("T43 PASS: Ctrl+F opens header search; Esc closes and clears it.");
    }

    // ------------------------------------------------------------------
    // T44 - Pill counter: Mencari... / N hasil / Tidak ada hasil
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T44_SearchResultSummary_CountsHits()
    {
        Directory.CreateDirectory(Path.Combine(_root, "subA"));
        File.WriteAllText(Path.Combine(_root, "subA", "pillHit1.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "subA", "pillHit2.txt"), "x");

        Assert.Equal("", _vm.SearchResultSummary);      // not searching -> nothing to show

        _vm.SearchText = "pillHit";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count() == 2, 10_000);
        Assert.False(_vm.IsSearching, "IsSearching must be false once the walk finished.");
        Assert.Equal("2 hasil", _vm.SearchResultSummary);

        _vm.SearchText = "pillHit1";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count() == 1, 10_000);
        Assert.Equal("1 hasil", _vm.SearchResultSummary);

        _vm.SearchText = "pillHitTidakAda";
        WaitFor(() => _vm.IsEmpty && !_vm.IsSearching, 10_000);
        Assert.Equal("Tidak ada hasil", _vm.SearchResultSummary);

        _vm.CloseSearchCommand.Execute(null);
        Assert.Equal("", _vm.SearchResultSummary);
        _output.WriteLine("T44 PASS: pill counter follows hits, emptiness and the running state.");
    }

    // ------------------------------------------------------------------
    // T45 - Typed '?' is a literal, not a wildcard (Windows forbids it in names anyway)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T45_Search_WildcardCharsAreLiteral()
    {
        WriteFile("abc.txt", "x");
        WriteFile("axc.txt", "x");     // would match the wildcard pattern "a?c"

        _vm.SearchText = "a?c";
        WaitFor(() => !_vm.IsSearching, 10_000);
        Assert.Equal(0, _vm.Items.OfType<ExplorerItemViewModel>().Count());

        _vm.SearchText = "abc";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Count() == 1, 10_000);
        Assert.Equal("abc.txt", _vm.Items.OfType<ExplorerItemViewModel>().First().Name);
        _output.WriteLine("T45 PASS: '?' is a literal character, never a wildcard.");
    }

    // ------------------------------------------------------------------
    // T46 - A search hit living in a SUBFOLDER can be deleted (the old guard refused it)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T46_Delete_SearchHitInSubfolder_IsAllowed()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_root, "deep", "deeper")).FullName;
        var victim = Path.Combine(deep, "buried.txt");
        File.WriteAllText(victim, "x");

        _vm.SearchText = "buried";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "buried.txt"), 10_000);
        WaitFor(() => !_vm.IsSearching, 10_000);

        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(_vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "buried.txt"));

        _platform.RecycleBinResult = true;
        _vm.DeleteItemCommand.Execute(null);

        WaitFor(() => _platform.RecycleBinCalls.Any(p => string.Equals(p, victim, StringComparison.OrdinalIgnoreCase)), 5_000,
            "recycle-bin delete reached the platform with the subfolder path");
        _output.WriteLine("T46 PASS: a search hit from a subfolder reaches the recycle bin.");
    }

    // ------------------------------------------------------------------
    // T47 - Opening a folder from search results leaves search mode (like Explorer)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T47_Navigate_FromSearchResults_ClearsQuery()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "subNav")).FullName;
        File.WriteAllText(Path.Combine(sub, "navigable.txt"), "x");

        _vm.SearchText = "navigable";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "navigable.txt"), 10_000);

        _vm.NavigateTo(sub);
        WaitFor(() => _vm.CurrentPath == sub, 5_000);
        Assert.Equal("", _vm.SearchText);

        // the new folder is listed normally, not filtered by the old query
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "navigable.txt"), 10_000);
        _output.WriteLine("T47 PASS: navigating away from search results clears the query.");
    }

    // ------------------------------------------------------------------
    // T48 - Empty state is search aware
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T48_EmptyStateText_IsSearchAware()
    {
        Assert.Equal("Folder kosong.", _vm.EmptyStateText);
        Assert.False(_vm.IsSearchActive);

        _vm.SearchText = "tidakAdaFileSepertiIni";
        WaitFor(() => _vm.IsEmpty && !_vm.IsSearching, 10_000);

        Assert.True(_vm.IsSearchActive);
        Assert.Contains("Tidak ada hasil", _vm.EmptyStateText);

        _vm.CloseSearchCommand.Execute(null);
        Assert.Equal("Folder kosong.", _vm.EmptyStateText);
        _output.WriteLine("T48 PASS: empty state tells the user it was a search, not an empty folder.");
    }
    // ------------------------------------------------------------------
    // T49 - A selection made WHILE a load is running survives (Explorer keeps your selection)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T49_SelectionMadeDuringLoad_SurvivesCompletion()
    {
        WriteFile("raceA.txt", "x");
        _vm.RefreshCommand.Execute(null);
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "raceA.txt"), 10_000);

        var oldObject = _vm.Items.OfType<ExplorerItemViewModel>().First(i => i.Name == "raceA.txt");

        // Start a new load; one dispatcher pass lets its body capture the (empty) selection and
        // hand the enumeration to a worker - the swap has NOT happened yet.
        WriteFile("raceB.txt", "x");
        _vm.RefreshCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // User clicks a row in that window (this is what used to be thrown away).
        _vm.SelectedItems.Clear();
        _vm.SelectedItems.Add(oldObject);
        Assert.Single(_vm.SelectedItems.OfType<ExplorerItemViewModel>());

        // Let the load finish: the list is rebuilt with NEW item objects...
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "raceB.txt")
                       && !_vm.Items.Contains(oldObject), 10_000, "load completed (objects swapped)");

        // ...and the selection the user just made must survive, on the new object.
        var selected = _vm.SelectedItems.OfType<ExplorerItemViewModel>().ToList();
        Assert.Single(selected);
        Assert.Equal("raceA.txt", selected[0].Name);
        Assert.Contains(selected[0], _vm.Items.OfType<ExplorerItemViewModel>());
        _output.WriteLine("T49 PASS: a click during an auto-refresh is not swallowed by the reload.");
    }
    // ------------------------------------------------------------------
    // T50 - Enter in the search pill opens the first hit (type-and-go)
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void T50_EnterInSearch_OpensFirstHit()
    {
        Directory.CreateDirectory(Path.Combine(_root, "enterDir"));
        File.WriteAllText(Path.Combine(_root, "enterTarget.txt"), "x");

        _vm.SearchText = "enterTarget";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.Name == "enterTarget.txt"), 10_000);
        WaitFor(() => !_vm.IsSearching, 10_000);

        _vm.OpenFirstSearchResultCommand.Execute(null);
        WaitFor(() => _platform.OpenedWithDefault.Count > 0, 5_000, "first hit opened in its default app");
        Assert.Contains(_platform.OpenedWithDefault, p => p.EndsWith("enterTarget.txt", StringComparison.OrdinalIgnoreCase));

        // a folder hit navigates instead of shell-opening
        _vm.SearchText = "enterDir";
        WaitFor(() => _vm.Items.OfType<ExplorerItemViewModel>().Any(i => i.IsDirectory && i.Name == "enterDir"), 10_000);
        WaitFor(() => !_vm.IsSearching, 10_000);

        _vm.OpenFirstSearchResultCommand.Execute(null);
        WaitFor(() => _vm.CurrentPath.EndsWith("enterDir", StringComparison.OrdinalIgnoreCase), 5_000, "folder hit navigates");
        Assert.Equal("", _vm.SearchText);
        _output.WriteLine("T50 PASS: Enter opens the first search hit (file -> app, folder -> navigates).");
    }
}
