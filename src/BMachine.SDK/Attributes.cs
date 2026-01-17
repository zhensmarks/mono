namespace BMachine.SDK;

/// <summary>
/// Attribute untuk menandai class sebagai plugin entry point
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PluginInfoAttribute : Attribute
{
    public string Id { get; }
    public string Name { get; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Icon { get; set; }
    public string[]? Dependencies { get; set; }
    
    public PluginInfoAttribute(string id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// Attribute untuk mendaftarkan menu entry
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class MenuEntryAttribute : Attribute
{
    public string Title { get; }
    public string? Icon { get; set; }
    public int Order { get; set; } = 100;
    
    public MenuEntryAttribute(string title)
    {
        Title = title;
    }
}

/// <summary>
/// Attribute untuk mendaftarkan dashboard widget
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class WidgetAttribute : Attribute
{
    public string Id { get; }
    public string Title { get; }
    public WidgetSize Size { get; set; } = WidgetSize.Medium;
    
    public WidgetAttribute(string id, string title)
    {
        Id = id;
        Title = title;
    }
}
