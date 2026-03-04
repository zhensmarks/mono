using System;
using System.Collections.Generic;

namespace TrelloCompact.Models;

public class AppSettings
{
    public string? TrelloApiKey { get; set; }
    public string? TrelloToken { get; set; }
    public string? DisplayName { get; set; }
    
    // Default move target
    public string? DefaultMoveBoardId { get; set; }
    public string? DefaultMoveBoardName { get; set; }
    public string? DefaultMoveListId { get; set; }
    public string? DefaultMoveListName { get; set; }
    
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 700;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public bool IsMaximized { get; set; }
    
    // Offline cache per list
    public Dictionary<string, string> CachedCards { get; set; } = new();
    
    // Manually added card IDs per list
    public Dictionary<string, List<string>> ManuallyAddedCards { get; set; } = new();
    
    // Notepad text per list
    public Dictionary<string, string> NotepadTexts { get; set; } = new();
    
    public List<CustomTab> Tabs { get; set; } = new();
}

public class CustomTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "NEW TAB";
    public string BoardId { get; set; } = "";
    public string BoardName { get; set; } = "";
    public string ListId { get; set; } = "";
    public string ListName { get; set; } = "";
    public string AccentColor { get; set; } = "#3b82f6";
    public int Order { get; set; } = 0;
}
