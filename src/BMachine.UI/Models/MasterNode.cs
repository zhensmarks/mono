using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Linq;

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

    public ObservableCollection<MasterNode> Children { get; } = new();
    
    // Lazy Loading Support
    public bool HasUnloadedChildren { get; private set; }
    private readonly Func<string, IEnumerable<MasterNode>>? _loadChildrenAction;

    public MasterNode(string path, bool isDirectory, Func<string, IEnumerable<MasterNode>>? loadChildrenAction = null)
    {
        FullPath = path;
        IsDirectory = isDirectory;
        _loadChildrenAction = loadChildrenAction;
        
        var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Name = Path.GetFileName(cleanPath);
        if (string.IsNullOrEmpty(Name)) Name = cleanPath;
        
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
