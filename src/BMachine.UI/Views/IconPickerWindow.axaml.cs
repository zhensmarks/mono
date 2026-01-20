using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class IconPickerWindow : Window
{
    private string _selectedKey = "";

    public class IconItem
    {
        public string Key { get; set; } = "";
        public StreamGeometry? Geometry { get; set; }
    }

    public IconPickerWindow()
    {
        InitializeComponent();
        LoadIcons();
    }

    private void LoadIcons()
    {
        var icons = new List<IconItem>();
        
        // Manual list of keys we added to App.axaml
        var keys = new[] 
        {
            "IconCamera", "IconPrint", "IconEdit", "IconLink", "IconBox", 
            "IconTrash", "IconTrash2", 
            "IconStar", "IconSync", "IconFolder", "IconFolderPlus", "IconFolderMinus", "IconFolderOpen",
            "IconMenu", "IconBolt", "IconPython", "IconScript", 
            "IconUser", "IconExtension", "IconSettings", "IconPalette",
            "IconHome", "IconSave", "IconCloud", "IconPlay", "IconPause", 
            "IconStop", "IconSkipBack", "IconSkipForward", "IconVolume", "IconVolumeX",
            "IconImage", "IconSearch", "IconCode", "IconTerminal", "IconType", "IconBold", "IconItalic", "IconUnderline",
            "IconLock", "IconUnlock", "IconClock", "IconCalendar", "IconHeart", 
            "IconShare", "IconCheck", "IconX",
            "IconFile", "IconFileText", "IconFilePlus", "IconFileMinus",
            "IconDownload", "IconUpload", "IconRefresh", "IconWifi", "IconBattery",
            
            // Extreme
            "IconFire", "IconSkull", "IconRadioactive", "IconWarning", 
            "IconLab", "IconBrain", "IconGhost", "IconRocket", "IconZapFilled",
            "IconGitBranch", "IconTerminalBash", "IconBug", "IconApi",
            
            // Thematic
            "IconBriefcase", "IconMapPin", "IconCreditCard", "IconGraduationCap", "IconGift", "IconFilm", "IconSun"
        };
        
        foreach(var key in keys)
        {
            if (Application.Current!.TryGetResource(key, null, out var res) && res is StreamGeometry geom)
            {
                icons.Add(new IconItem { Key = key, Geometry = geom });
            }
        }
        
        var listControl = this.FindControl<ItemsControl>("IconList");
        if (listControl != null)
        {
            listControl.ItemsSource = icons;
        }
    }

    private void OnIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            Close(key);
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Close(""); // Return empty to clear
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null); // Return null to cancel (no change)
    }
}
