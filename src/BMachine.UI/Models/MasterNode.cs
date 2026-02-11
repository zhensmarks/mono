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

    public MasterNode(string path, bool isDirectory)
    {
        FullPath = path;
        IsDirectory = isDirectory;
        
        var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Name = Path.GetFileName(cleanPath);
        if (string.IsNullOrEmpty(Name)) Name = cleanPath;
        
        IsExpanded = false; // Default collapsed
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
}
