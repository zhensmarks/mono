using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BMachine.UI.Models;

/// <summary>
/// Represents a single item in the Radial Menu.
/// </summary>
public partial class RadialMenuItem : ObservableObject
{
    /// <summary>
    /// Full name of the script (e.g., "PROFESI", "REPLACE PAS FOTO")
    /// </summary>
    [ObservableProperty]
    private string _fullName = string.Empty;

    /// <summary>
    /// Short name for display in radial menu (e.g., "P", "RPF")
    /// </summary>
    [ObservableProperty]
    private string _shortName = string.Empty;

    /// <summary>
    /// Path to the script file
    /// </summary>
    [ObservableProperty]
    private string _scriptPath = string.Empty;

    /// <summary>
    /// True = Master script, False = Action script
    /// </summary>
    [ObservableProperty]
    private bool _isMaster;

    /// <summary>
    /// Whether this item is currently highlighted (mouse over)
    /// </summary>
    [ObservableProperty]
    private bool _isHighlighted;

    // Position on canvas
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    
    // Angle for highlight detection (0 = North/Up)
    [ObservableProperty] private double _angle;

    [ObservableProperty] private int _order;
    
    [ObservableProperty] private Avalonia.Media.StreamGeometry? _icon;

    // --- New properties for redesign ---

    /// <summary>
    /// Label position X (for hover label placement)
    /// </summary>
    [ObservableProperty] private double _labelX;
    
    /// <summary>
    /// Label position Y (for hover label placement)
    /// </summary>
    [ObservableProperty] private double _labelY;

    /// <summary>
    /// Horizontal alignment hint for the label: "Left" or "Right"
    /// Based on which side of the circle the item is on.
    /// </summary>
    [ObservableProperty] private string _labelSide = "Right";

    /// <summary>
    /// Start angle for the pie wedge highlight (in degrees, 0 = top)
    /// </summary>
    [ObservableProperty] private double _wedgeStartAngle;

    /// <summary>
    /// Sweep angle for the pie wedge highlight (in degrees)
    /// </summary>
    [ObservableProperty] private double _wedgeSweepAngle;

    /// <summary>
        /// Whether this is a special navigation item (More/Back)
        /// </summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMoreDots))] private bool _isNavigation;

        /// <summary>
        /// Navigation type: "more" or "back"
        /// </summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMoreDots))] private string _navigationType = string.Empty;

        /// <summary>
                /// True = item ini bisa membuka submenu tab DOC (SEND-TEXT / SEND-LOGO
                /// saat ada lebih dari satu tab DOC di tab DOC).
                /// </summary>
                [ObservableProperty] private bool _hasSubmenu;

                /// <summary>
                /// Id tab DOC yang dipilih pada submenu (terisi hanya di item submenu;
                /// "" = pakai tab aktif seperti biasa).
                /// </summary>
                [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOuterItem))] private string _tabId = string.Empty;

                /// <summary>
                /// True = segmen cincin LUAR (pilihan tab DOC). Tampilan segmen luar
                /// mengikuti gaya menu dalam (band + garis + wedge accent); properti ini
                /// penanda identitas item untuk keperluan lain.
                /// </summary>
                public bool IsOuterItem => !string.IsNullOrEmpty(TabId);

                /// <summary>
                /// Sudut rotasi teks (dalam derajat) agar mengikuti lengkungan orientasi segmen.
                /// </summary>
                [ObservableProperty] private double _textRotation;

                /// <summary>
                /// True hanya untuk tombol "More" (tiga titik).
        /// Dipakai XAML: PathIcon menempelkan bbox geometri yang pipih ke ATAS kotak ikon
        /// (terukur -6.5 px dari pusat), jadi tombol More menggambar tiga titik manual
        /// (Ellipse di StackPanel center) supaya pasti center di lingkaran tombolnya.
        /// </summary>
        public bool IsMoreDots => IsNavigation && NavigationType == "more";

    /// <summary>
    /// Generate short name from full name automatically.
    /// "PROFESI" → "P"
    /// "REPLACE PAS FOTO" → "RPF"
    /// "REPLACE PAS FOTO 4X6" → "RPF4"
    /// </summary>
    public static string GenerateShortName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";

        // Logic Baru: Jika nama pendek (<= 4 huruf), gunakan langsung (misal "PAS", "PSD", "EDIT")
        if (fullName.Length <= 4)
        {
            return fullName.ToUpper();
        }

        var parts = fullName.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // Ambil huruf pertama dari 2 kata pertama
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpper();
        }
        else if (fullName.Length >= 2)
        {
            // Ambil 2 huruf pertama
            return fullName.Substring(0, 2).ToUpper();
        }
        else
        {
            return fullName.Substring(0, 1).ToUpper();
        }
    }
}
