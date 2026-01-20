using System.Text.Json.Serialization;

namespace BMachine.UI.Models;

public enum TriggerType
{
    Mouse,
    Keyboard
}

public class TriggerConfig
{
    public TriggerType Type { get; set; } = TriggerType.Mouse;
    
    // For Mouse
    public int MouseButton { get; set; } = 2; // 0=Left, 1=Right, 2=Middle, 3=X1, 4=X2
    
    // For Keyboard
    public int Key { get; set; } // Virtual Key Code
    
    // Modifiers (Bitmask: 1=Alt, 2=Ctrl, 4=Shift, 8=Win)
    public int Modifiers { get; set; } = 4; // Default Shift
    
    [JsonIgnore]
    public bool HasAlt => (Modifiers & 1) != 0;
    [JsonIgnore]
    public bool HasCtrl => (Modifiers & 2) != 0;
    [JsonIgnore]
    public bool HasShift => (Modifiers & 4) != 0;
    [JsonIgnore]
    public bool HasWin => (Modifiers & 8) != 0;

    public override string ToString()
    {
        var mods = new System.Collections.Generic.List<string>();
        if (HasCtrl) mods.Add("Ctrl");
        if (HasShift) mods.Add("Shift");
        if (HasAlt) mods.Add("Alt");
        if (HasWin) mods.Add("Win");
        
        string main = Type == TriggerType.Mouse ? $"Mouse {MouseButton}" : $"Key {Key}";
        if (Type == TriggerType.Mouse)
        {
            if (MouseButton == 0) main = "Left Click";
            else if (MouseButton == 1) main = "Right Click";
            else if (MouseButton == 2) main = "Middle Click";
            else if (MouseButton == 3) main = "XButton 1";
            else if (MouseButton == 4) main = "XButton 2";
        }
        else
        {
            main = ((System.ConsoleKey)Key).ToString();
        }

        return mods.Count > 0 ? $"{string.Join(" + ", mods)} + {main}" : main;
    }
}
