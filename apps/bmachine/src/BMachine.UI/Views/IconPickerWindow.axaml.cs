using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace BMachine.UI.Views;

public partial class IconPickerWindow : Window
{
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

        // One entry per distinct Tabler outline geometry, maintained from the shared dictionary.
        var keys = new[]
        {
            "IconHome2", "IconUserCircle", "IconSettings2", "IconSearch2",
            "IconEye", "IconEyeOff",
            "IconMenu2", "IconBell", "IconMoon", "IconSun",
            "IconArrowDown", "IconArrowLeft", "IconArrowRight", "IconArrowUp",
            "IconChevronDown", "IconChevronLeft", "IconChevronRight", "IconChevronUp",
            "IconDots", "IconDotsVertical", "IconExternalLink", "IconFolder",
            "IconFolderOpen", "IconFolderPlus", "IconFolderMinus", "IconFile",
            "IconFileText", "IconFileCode", "IconFilePlus", "IconFileMinus",
            "IconClipboard", "IconCopy", "IconPaperclip", "IconDownload",
            "IconUpload", "IconDeviceFloppy", "IconPrinter", "IconCloud",
            "IconCloudUpload", "IconPencil", "IconEditSquare", "IconBrush",
            "IconScissors", "IconEraser", "IconFilter", "IconPalette", "IconCamera",
            "IconPhoto", "IconMovie", "IconMusic", "IconPlayerPlay",
            "IconPlayerPause", "IconPlayerStop", "IconPlayerRecord", "IconPlayerSkipBack",
            "IconPlayerSkipForward", "IconVolume", "IconVolumeOff", "IconReload",
            "IconPower", "IconBrandGithub", "IconBrandPython", "IconCode",
            "IconTerminal", "IconApi", "IconGitBranch", "IconBug",
            "IconDatabase", "IconServer", "IconCpu", "IconRobot",
            "IconExtension", "IconBolt", "IconActivity", "IconMail",
            "IconMessage", "IconSend", "IconAlertCircle", "IconAlertTriangle",
            "IconCheck", "IconX", "IconShieldCheck", "IconLock",
            "IconLockOpen", "IconLogin", "IconLogout", "IconInfoCircle", "IconWifi", "IconBattery",
            "IconBook", "IconBookmark", "IconCalendar", "IconClock",
            "IconCoins", "IconCreditCard", "IconHeart", "IconStar",
            "IconFlame", "IconBox", "IconBriefcase", "IconGift",
            "IconGraduationCap", "IconMap", "IconMapPin", "IconRadioactive", "IconLab",
            "IconBrain", "IconGhost", "IconRocket", "IconSkull",
            "IconSquare", "IconGrid", "IconLayers", "IconList", "IconCheckbox", "IconChecklist",
            "IconCirclePlus", "IconPlus", "IconGripVertical", "IconShare",
            "IconType", "IconBold", "IconItalic", "IconUnderline",
            "IconLink", "IconTrash",
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
