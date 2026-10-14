using Avalonia.Media;

namespace BMachine.UI.Services;

public static class AppFonts
{
    public const string Inter = "Inter";
    public const string JetBrainsMono = "JetBrains Mono";

    public static readonly string[] Families = { Inter, JetBrainsMono };

    public static readonly FontFamily Primary = new("avares://BMachine.UI/Assets/Fonts#Inter");
    public static readonly FontFamily Mono = new("avares://BMachine.UI/Assets/Fonts#JetBrains Mono");

    public static bool TryCreate(string? name, out FontFamily font)
    {
        font = Primary;
        if (name == JetBrainsMono)
        {
            font = Mono;
            return true;
        }
        return name == Inter;
    }
}