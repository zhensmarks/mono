using Avalonia.Media;

namespace BMachine.UI.Models;

public class BatchScriptOption
{
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = ""; // Original Filename
    public string Path { get; set; } = "";
    public StreamGeometry? IconGeometry { get; set; }
}
