using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace BMachine.UI.Models;

/// <summary>
/// Recursive model for Master File Browser (Folders and Files).
/// </summary>
public partial class MasterNode : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    
    // Group name is essentially the parent folder name, useful for flat search if needed, 
    // but in tree view we rely on structure.
    
    [ObservableProperty]
    private bool _isExpanded;

    // --- Thumbnail (lazy, JPG preview of a PSD/PSB file) ---

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _thumbnailLoaded;

    /// <summary>
    /// Path to the JPG/JPEG preview sitting next to this file (same base name).
    /// Null when the node is a directory or has no matching preview.
    /// </summary>
    public string? ThumbnailSourcePath
    {
        get => _thumbnailSourcePath;
        set
        {
            if (SetProperty(ref _thumbnailSourcePath, value))
                OnPropertyChanged(nameof(HasPreview));
        }
    }

    private string? _thumbnailSourcePath;

    /// <summary>
    /// True when this leaf file has a paired JPG preview and should therefore
    /// render as a thumbnail card in the Master browser (instead of a row).
    /// </summary>
    public bool HasPreview => !IsDirectory && _thumbnailSourcePath != null;

    public ObservableCollection<MasterNode> Children { get; } = new();
    
    // Lazy Loading Support
    public bool HasUnloadedChildren { get; private set; }
    private readonly Func<string, IEnumerable<MasterNode>>? _loadChildrenAction;

    public MasterNode(string path, bool isDirectory, Func<string, IEnumerable<MasterNode>>? loadChildrenAction = null, string? customDisplayName = null)
    {
        FullPath = path;
        IsDirectory = isDirectory;
        _loadChildrenAction = loadChildrenAction;
        
        if (!string.IsNullOrWhiteSpace(customDisplayName))
        {
            Name = customDisplayName;
        }
        else
        {
            var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Name = Path.GetFileName(cleanPath);
            if (string.IsNullOrEmpty(Name)) Name = cleanPath;
        }
        
        IsExpanded = false; 

        // If directory and loader provided, check if it has content (fast check)
        // Ideally we just assume it has children if it's a directory to show the expander,
        // then fail gracefully if empty upon expansion.
        if (IsDirectory)
        {
            HasUnloadedChildren = true;
            Children.Add(new MasterNode("Loading...", false)); // Dummy
        }
    }

    // For files (leaf nodes)
    public MasterNode(string path, bool isDirectory) : this(path, isDirectory, null)
    {
        HasUnloadedChildren = false;
        Children.Clear();
    }

    async partial void OnIsExpandedChanged(bool value)
    {
        if (value && HasUnloadedChildren && _loadChildrenAction != null)
        {
            HasUnloadedChildren = false;
            
            // Dispatch Clear to UI Thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Children.Clear());
            
            try
            {
                // Run loader in background to avoid UI freeze
                var nodes = await System.Threading.Tasks.Task.Run(() => _loadChildrenAction(FullPath));
                
                // Dispatch Add to UI Thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                {
                    foreach (var node in nodes)
                    {
                        Children.Add(node);
                    }
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading children for {Name}: {ex.Message}");
                // Optionally add a dummy error node
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => 
                    Children.Add(new MasterNode($"Error: {ex.Message}", false)));
            }
        }
    }

    /// <summary>
    /// Decodes this node's JPG preview (if any) on a background thread and
    /// assigns it to <see cref="Thumbnail"/>. Safe to call repeatedly.
    /// </summary>
    public async Task LoadThumbnailAsync()
    {
        if (ThumbnailLoaded || IsDirectory) return;
        if (string.IsNullOrEmpty(ThumbnailSourcePath)) return;

        ThumbnailLoaded = true;

        var jpg = ThumbnailSourcePath;
        var bitmap = await Services.ThumbnailCacheService.GetThumbnailAsync(jpg);
        if (bitmap == null) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bitmap);
    }

    /// <summary>
    /// Recursively yields every leaf (non-directory) node beneath this node,
    /// loading unloaded children synchronously. Intended to be called from a
    /// background thread so the flat thumbnail list is complete in one pass.
    /// </summary>
    public IEnumerable<MasterNode> EnumerateLeaves()
    {
        if (!IsDirectory)
        {
            yield return this;
            yield break;
        }

        if (HasUnloadedChildren && _loadChildrenAction != null)
        {
            HasUnloadedChildren = false;
            Children.Clear();

            try
            {
                foreach (var child in _loadChildrenAction(FullPath))
                    Children.Add(child);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading children for {Name}: {ex.Message}");
            }
        }

        foreach (var child in Children.ToArray())
        {
            foreach (var leaf in child.EnumerateLeaves())
                yield return leaf;
        }
    }

    /// <summary>
    /// Command wired to the UI so a thumbnail is decoded only when the node
    /// actually becomes visible in the tree.
    /// </summary>
    public CommunityToolkit.Mvvm.Input.IRelayCommand LoadThumbnailCommand =>
        _loadThumbnailCommand ??= new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(LoadThumbnailAsync);

    private CommunityToolkit.Mvvm.Input.IRelayCommand? _loadThumbnailCommand;

    /// <summary>
    /// Helper to sort: Folders first, then Files.
    /// </summary>
    public void SortChildren()
    {
        var sorted = Children.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();
        Children.Clear();
        foreach (var item in sorted) Children.Add(item);
    }

    /// <summary>
    /// Manually set children (e.g. for pre-filtered search results).
    /// </summary>
    public void SetChildren(IEnumerable<MasterNode> children)
    {
        HasUnloadedChildren = false;
        Children.Clear();
        foreach (var child in children) Children.Add(child);
    }
}
