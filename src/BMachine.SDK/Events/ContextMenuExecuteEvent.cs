namespace BMachine.SDK;

public class ContextMenuExecuteEvent : IEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public string Source => "Context Menu Plugin";
    
    public string ScriptKey { get; set; } = "";
    public string FolderPath { get; set; } = "";
}
