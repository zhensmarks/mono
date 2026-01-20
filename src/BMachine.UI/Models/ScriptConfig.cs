namespace BMachine.UI.Models;

public class ScriptConfig
{
    public string Name { get; set; } = ""; // Display Name
    public string Code { get; set; } = ""; // Short Code
    public string IconKey { get; set; } = ""; // Icon Resource Key
    public int Order { get; set; } = 0;    // Sort Order
}
