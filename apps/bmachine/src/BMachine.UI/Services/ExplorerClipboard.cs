using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace BMachine.UI.Services;

/// <summary>
/// Clipboard access used by the explorer. Abstracted so the copy/cut/paste logic can be
/// unit-tested without a real desktop clipboard (headless tests have no MainWindow).
/// </summary>
public interface IExplorerClipboard
{
    /// <summary>Publishes file/folder paths (FileNames + Text). Returns false when no clipboard is available.</summary>
    Task<bool> SetFilePathsAsync(IReadOnlyList<string> paths);

    Task<bool> SetTextAsync(string text);

    Task<string?> GetTextAsync();

    /// <summary>Reads file/folder paths from any of the formats a clipboard may expose, or null when none.</summary>
    Task<IReadOnlyList<string>?> GetFilePathsAsync();

    Task<bool> HasFilePayloadAsync();

    Task ClearAsync();
}

/// <summary>Default implementation: the real OS clipboard of the app's main window.</summary>
public sealed class AvaloniaExplorerClipboard : IExplorerClipboard
{
    private static IClipboard? GetClipboard()
        => Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;

    public async Task<bool> SetFilePathsAsync(IReadOnlyList<string> paths)
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return false;

        var data = new DataObject();
        data.Set(DataFormats.FileNames, paths.ToArray());
        data.Set(DataFormats.Text, string.Join(Environment.NewLine, paths));
        await clipboard.SetDataObjectAsync(data);
        return true;
    }

    public async Task<bool> SetTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }

    public async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return null;
        try { return await clipboard.GetTextAsync(); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<string>?> GetFilePathsAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return null;

        try
        {
            var formats = await clipboard.GetFormatsAsync();
            List<string>? list = null;

            if (list == null && formats.Contains(DataFormats.FileNames))
                list = ToPathList(await clipboard.GetDataAsync(DataFormats.FileNames));
            if (list == null && formats.Contains(DataFormats.Files))
                list = ToPathList(await clipboard.GetDataAsync(DataFormats.Files));
            if (list == null && formats.Contains("Files"))
                list = ToPathList(await clipboard.GetDataAsync("Files"));

            // Fallback: plain text containing paths (what we also publish on Copy)
            if (list == null && formats.Contains(DataFormats.Text))
            {
                var text = await clipboard.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Any(l => File.Exists(l.Trim()) || Directory.Exists(l.Trim())))
                        list = lines.Select(l => l.Trim()).ToList();
                }
            }

            return list;
        }
        catch { return null; }
    }

    public async Task<bool> HasFilePayloadAsync() => (await GetFilePathsAsync())?.Count > 0;

    public async Task ClearAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return;
        try { await clipboard.ClearAsync(); } catch { /* best effort */ }
    }

    private static List<string>? ToPathList(object? fileData)
    {
        if (fileData == null) return null;

        if (fileData is IEnumerable<string> enumStr)
            return enumStr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        if (fileData is string[] arr)
            return arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        if (fileData is System.Collections.IEnumerable enumerable)
        {
            var list = new List<string>();
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                if (item is Avalonia.Platform.Storage.IStorageItem storageItem)
                {
                    var path = storageItem.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path)) list.Add(path);
                }
                else if (item is System.IO.FileInfo fi) list.Add(fi.FullName);
                else if (item is System.IO.DirectoryInfo di) list.Add(di.FullName);
                else
                {
                    var str = item.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(str)) list.Add(str);
                }
            }
            if (list.Count > 0) return list;
        }

        return null;
    }
}
