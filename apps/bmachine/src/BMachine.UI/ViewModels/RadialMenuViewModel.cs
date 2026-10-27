using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.SDK;
using BMachine.UI.Models;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;

namespace BMachine.UI.ViewModels;

public partial class RadialMenuViewModel : ObservableObject, CommunityToolkit.Mvvm.Messaging.IRecipient<BMachine.UI.Messages.ScriptOrderChangedMessage>
{
    private readonly IDatabase? _database;
    private readonly Services.IProcessLogService? _logService;
    private readonly BMachine.Core.Platform.IPlatformService _platformService;
    
    private const int MaxVisibleItems = 7;
    private const double CanvasSize = 200.0;
    private const double CenterX = 100.0;
    private const double CenterY = 100.0;
    private const double ItemRadius = 72.0;    // Distance from center to item (Middle of 44 inward and 100 outward)
    private const double ButtonSize = 36.0;
    private const double LabelOffset = 30.0;   // Extra distance for label from item center
    
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private Thickness _menuMargin; 

    public ObservableCollection<RadialMenuItem> MasterItems { get; } = new();
    public ObservableCollection<RadialMenuItem> ActionItems { get; } = new();
    
    /// <summary>
    /// Items currently visible on screen (subset based on current page)
    /// </summary>
    public ObservableCollection<RadialMenuItem> VisibleItems { get; } = new();

    /// <summary>
    /// Cincin luar: pilihan tab DOC yang muncul mengelilingi item SEND-TEXT /
    /// SEND-LOGO. Menu utama (VisibleItems) tidak pernah diganti.
    /// </summary>
    public ObservableCollection<RadialMenuItem> SubmenuItems { get; } = new();

    // Manual property — not using [ObservableProperty] because HighlightItem 
    // needs direct field access to avoid MVVMTK0034 warnings
    private RadialMenuItem? _currentHighlightedItem;
    public RadialMenuItem? HighlightedItem
    {
        get => _currentHighlightedItem;
        private set => SetProperty(ref _currentHighlightedItem, value);
    }

    [ObservableProperty] private string _customScriptsPath = "";
    
    /// <summary>
    /// Current page: 0 = main, 1 = more
    /// </summary>
    [ObservableProperty] private int _currentPage;
    private DateTime _lastPageSwitchTime = DateTime.MinValue;

    // --- Submenu tab DOC (SEND-TEXT / SEND-LOGO) -------------------------
    private static bool IsDocActionScript(string fileName, string displayName)
    {
        var normFile = fileName.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
        if (normFile.StartsWith("sendtext") || normFile.StartsWith("sendlogo"))
            return true;
        var normName = displayName.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
        if (normName.Contains("sendtext") || normName.Contains("sendlogo"))
            return true;
        return false;
    }

    /// <summary>Script induk submenu; "" = tidak sedang di submenu.</summary>
    private string _submenuScriptPath = "";

    /// <summary>Batas dalam band luar (px dari pusat jendela 320x320).</summary>
    private const double OuterBandMin = 102.0;
    /// <summary>Batas luar band luar (px dari pusat jendela 320x320).</summary>
    private const double OuterBandMax = 154.0;
    /// <summary>Tab hasil query terakhir (dipakai saat membangun item submenu).</summary>
    private List<Messages.DocTabSnapshot>? _submenuTabs;
    /// <summary>True kalau busur luar tab DOC sedang tampil.</summary>
    public bool HasOuterRing => SubmenuItems.Count > 0;

    /// <summary>Item yang sedang didwell untuk membuka submenu.</summary>
    private RadialMenuItem? _dwellCandidate;
    private Avalonia.Threading.DispatcherTimer? _submenuDwellTimer;

    /// <summary>Dwell hover (ms) sebelum submenu tab DOC muncul.</summary>
    private const int SubmenuDwellMs = 300;
    
    /// <summary>
    /// Whether the total item count exceeds MaxVisibleItems
    /// </summary>
    [ObservableProperty] private bool _hasMoreItems;

    /// <summary>
    /// Start angle of the highlighted wedge (for PieWedge binding)
    /// </summary>
    [ObservableProperty] private double _wedgeStart;

    /// <summary>
    /// Sweep angle of the highlighted wedge (for PieWedge binding)
    /// </summary>
    [ObservableProperty] private double _wedgeSweep;

    /// <summary>
    /// Whether the pie wedge should be visible
    /// </summary>
    [ObservableProperty] private bool _isWedgeVisible;

    /// <summary>
    /// Label text for currently highlighted item
    /// </summary>
    [ObservableProperty] private string _highlightLabel = "";

    /// <summary>
    /// X position for the highlight label
    /// </summary>
    [ObservableProperty] private double _highlightLabelX;

    /// <summary>
    /// Y position for the highlight label
    /// </summary>
    [ObservableProperty] private double _highlightLabelY;

    /// <summary>
    /// Whether label is on left side (affects alignment)
    /// </summary>
    [ObservableProperty] private bool _isLabelOnLeft;

    // Center Dynamic Texts
    [ObservableProperty] private string _centerTextTitle = "";
    [ObservableProperty] private string _centerTextSubtitle = "";

    public RadialMenuViewModel(IDatabase? database, Services.IProcessLogService? logService = null, BMachine.Core.Platform.IPlatformService? platformService = null)
    {
        _database = database;
        _logService = logService;
        _platformService = platformService ?? BMachine.Core.Platform.PlatformServiceFactory.Get();
        
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.RegisterAll(this);

        SubmenuItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOuterRing));

        LoadScripts();
    }
    
    public void Receive(BMachine.UI.Messages.ScriptOrderChangedMessage message)
    {
        LoadScripts();
    }

    public void ReloadScripts()
    {
        LoadScripts();
    }

    private void LoadScripts()
        {
            // Bersihkan cincin luar tab DOC kalau script dimuat ulang.
            ClearSubmenu();

            MasterItems.Clear();
        ActionItems.Clear();
        LoadMetadata();

        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        
        // Action Scripts Only (.jsx and .pyw)
        var actionPath = Path.Combine(baseDir, "Action");
        if (Directory.Exists(actionPath))
        {
            foreach (var f in Directory.GetFiles(actionPath, "*.jsx")) AddItem(ActionItems, f, false);
            foreach (var f in Directory.GetFiles(actionPath, "*.pyw")) AddItem(ActionItems, f, false);
        }
        if (Directory.Exists(baseDir))
        {
            foreach (var f in Directory.GetFiles(baseDir, "*.pyw")) AddItem(ActionItems, f, false);
        }

        // Sort ActionItems
        var sorted = ActionItems.OrderBy(x => x.Order).ThenBy(x => x.FullName).ToList();
        ActionItems.Clear();
        foreach (var item in sorted) ActionItems.Add(item);

        HasMoreItems = ActionItems.Count > MaxVisibleItems;
        CurrentPage = 0;
        BuildVisibleItems();
    }

    /// <summary>
    /// Rebuild VisibleItems based on CurrentPage
    /// </summary>
    private static Avalonia.Media.StreamGeometry? ResolveIcon(string key)
    {
        if (Application.Current?.TryGetResource(key, null, out var resource) == true)
            return resource as Avalonia.Media.StreamGeometry;

        return null;
    }

    private void BuildVisibleItems()
            {
                StopSubmenuDwell(); // dwell batal saat isi menu diganti
                VisibleItems.Clear();
                HighlightItem(null);

            if (ActionItems.Count <= MaxVisibleItems)
        {
            // All items fit — no More button needed
            foreach (var item in ActionItems)
            {
                item.IsNavigation = false;
                item.NavigationType = "";
                VisibleItems.Add(item);
            }
        }
        else if (CurrentPage == 0)
        {
            // Page 1: first 7 items + More button
            for (int i = 0; i < Math.Min(MaxVisibleItems, ActionItems.Count); i++)
            {
                ActionItems[i].IsNavigation = false;
                ActionItems[i].NavigationType = "";
                VisibleItems.Add(ActionItems[i]);
            }
            
            // Add "More" navigation item
            VisibleItems.Add(new RadialMenuItem
            {
                FullName = "More",
                ShortName = "MORE",
                Icon = ResolveIcon("IconMoreHorizontal"),
                IsNavigation = true,
                NavigationType = "more"
            });
        }
        else
        {
            // Page 2: remaining items + Back button
            for (int i = MaxVisibleItems; i < ActionItems.Count; i++)
            {
                ActionItems[i].IsNavigation = false;
                ActionItems[i].NavigationType = "";
                VisibleItems.Add(ActionItems[i]);
            }
            
            // Add "Back" navigation item
            VisibleItems.Add(new RadialMenuItem
            {
                FullName = "Back",
                ShortName = "BACK",
                Icon = ResolveIcon("IconArrowLeft"),
                IsNavigation = true,
                NavigationType = "back"
            });
        }

        ArrangeItems();
    }

    /// <summary>
        /// Cincin luar: satu segmen per tab DOC (teks = nomor tab, hub = judul
        /// tab), digelar mengelilingi item induk pada grid 45°.
        /// </summary>
        private void BuildOuterSubmenu(double parentAngle)
        {
            SubmenuItems.Clear();
            var tabs = _submenuTabs ?? new List<Messages.DocTabSnapshot>();
            int n = tabs.Count;
            if (n == 0) return;

            // Busur segmen luar simetris di sekitar sudut item induk:
            // n=2 -> Tab 1 di parent - 22.5°, Tab 2 di parent + 22.5°
            // Garis batas antar-tab tepat menunjuk keluar dari sudut parentAngle.
            double start = parentAngle - 45.0 * (n - 1) / 2.0;
            for (int i = 0; i < n; i++)
            {
                var tab = tabs[i];
                var item = new RadialMenuItem
                {
                    FullName = string.IsNullOrWhiteSpace(tab.Title) ? $"Tab {tab.Number}" : tab.Title,
                    ShortName = tab.Number.ToString(),
                    ScriptPath = _submenuScriptPath,
                    TabId = tab.Id,
                    Angle = ((start + i * 45.0) % 360.0 + 360.0) % 360.0
                };
                PlaceOuterItem(item, 128.0);
                SubmenuItems.Add(item);
            }
        }

        /// <summary>Hitung posisi X/Y dan sudut rotasi teks di kanvas 320x320 untuk segmen cincin luar.</summary>
        private static void PlaceOuterItem(RadialMenuItem item, double radius)
        {
            double trigAngleRad = (item.Angle - 90.0) * (Math.PI / 180.0);
            double itemCenterX = 160.0 + radius * Math.Cos(trigAngleRad);
            double itemCenterY = 160.0 + radius * Math.Sin(trigAngleRad);
            // Kotak konten 60x28 berpusat tepat di (itemCenterX, itemCenterY)
            item.X = itemCenterX - 30.0;
            item.Y = itemCenterY - 14.0;
            item.WedgeSweepAngle = 45.0;
            item.WedgeStartAngle = ((item.Angle - 22.5) % 360.0 + 360.0) % 360.0;

            // Sudut rotasi tangensial agar teks mengikuti lengkungan dan arah segmen
            double rot = item.Angle;
            if (rot > 180) rot -= 360;
            if (rot > 90) rot -= 180;
            else if (rot < -90) rot += 180;
            item.TextRotation = rot;
        }

        /// <summary>Tutup cincin luar (dipanggil saat menu dibuka ulang / reload).</summary>
        private void ClearSubmenu()
        {
            _submenuScriptPath = "";
            _submenuTabs = null;
            SubmenuItems.Clear();

            StopSubmenuDwell();
        }

        /// <summary>
        /// Arrange items in a full circle (360°), evenly spaced.
    /// The "More" / "Back" button is always placed at the bottom (270° in trig = South).
    /// </summary>
    private void ArrangeItems()
    {
        if (VisibleItems.Count == 0) return;

        // We use a fixed 8-slot grid corresponding to 0, 45, 90, 135, 180, 225, 270, 315.
        // Navigation item is ALWAYS at 180 (South).
        // Other items fill slots closest to 0 (North) first.
        int[] orderedSlots = new int[] { 0, 45, 315, 90, 270, 135, 225 };

        var nonNavItems = VisibleItems.Where(i => !i.IsNavigation).ToList();
        var sortedNonNav = nonNavItems.Take(orderedSlots.Length).ToList();
        
        // Assign slots 
        for (int i = 0; i < sortedNonNav.Count; i++)
        {
            sortedNonNav[i].Angle = orderedSlots[i];
        }

        foreach (var item in VisibleItems)
        {
            if (item.IsNavigation)
            {
                item.Angle = 180;
            }

            double angleDeg = item.Angle;
            
            // Convert "0=Top" system to standard trig
            double trigAngleDeg = angleDeg - 90;
            double trigAngleRad = trigAngleDeg * (Math.PI / 180.0);

            // Item position
            double itemCenterX = CenterX + ItemRadius * Math.Cos(trigAngleRad);
            double itemCenterY = CenterY + ItemRadius * Math.Sin(trigAngleRad);
            item.X = itemCenterX - ButtonSize / 2;
            item.Y = itemCenterY - ButtonSize / 2;

            // Label position (further out from center)
            double labelDist = ItemRadius + LabelOffset;
            double labelCenterX = CenterX + labelDist * Math.Cos(trigAngleRad);
            double labelCenterY = CenterY + labelDist * Math.Sin(trigAngleRad);
            item.LabelX = labelCenterX;
            item.LabelY = labelCenterY;

            // Determine label side based on angle
            item.LabelSide = (angleDeg > 180 && angleDeg < 360) ? "Left" : "Right";
            
            // Assign wedge size for the highlight
            item.WedgeSweepAngle = 45.0; // Fixed sweep for the 8-slice pie grid
            item.WedgeStartAngle = angleDeg - 22.5; 
        }
    }

    /// <summary>Cari item yang paling dekat sudutnya (hitungan 0 = Utara, searah jarum jam).</summary>
    private static RadialMenuItem? ClosestByAngle(IEnumerable<RadialMenuItem> items, double angle, out double minDiff)
    {
        RadialMenuItem? closest = null;
        minDiff = double.MaxValue;
        foreach (var item in items)
        {
            double diff = Math.Abs(angle - item.Angle);
            if (diff > 180) diff = 360 - diff;
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = item;
            }
        }
        return closest;
    }

    public void UpdateHighlight(Point mousePos, Size windowSize)
    {
        double cx = windowSize.Width / 2;
        double cy = windowSize.Height / 2;

        double dx = mousePos.X - cx;
        double dy = mousePos.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // Deadzone: center (< 40px) or outside window bounds (> 145px)
        if (dist < 40 || dist > OuterBandMax)
        {
            HighlightItem(null);
            UpdateSubmenuDwell(null);
            return;
        }

        // Calculate angle (0 = North/Top, clockwise)
        double angle = Math.Atan2(dy, dx) * (180 / Math.PI);
        angle += 90;
        if (angle < 0) angle += 360;

        // Band luar (cincin pilihan tab DOC) aktif: yang bisa dipilih hanya
        // segmen luar — menu utama tetap tampil tapi tidak ikut ter-highlight.
        if (SubmenuItems.Count > 0 && dist >= OuterBandMin)
        {
            var outer = ClosestByAngle(SubmenuItems, angle, out var outerDiff);
            double outerStep = Math.Min(360.0 / SubmenuItems.Count, 45.0);
            HighlightItem(outer != null && outerDiff < outerStep / 2.0 ? outer : null);
            UpdateSubmenuDwell(_currentHighlightedItem);
            return;
        }

        // Band dalam: item menu utama seperti biasa.
        var closest = ClosestByAngle(VisibleItems, angle, out var minDiff);
        double angleStep = VisibleItems.Count > 0 ? Math.Min(360.0 / VisibleItems.Count, 45.0) : 45.0;
        double threshold = angleStep / 2.0;

        if (closest != null && minDiff < threshold)
        {
            // Handle navigation items on hover with hysteresis
            if (closest.IsNavigation && closest != _currentHighlightedItem)
            {
                // Debounce: ignore nav hover within 400ms of last page switch
                var now = DateTime.UtcNow;
                if ((now - _lastPageSwitchTime).TotalMilliseconds < 400)
                    return;

                if (closest.NavigationType == "more")
                {
                    _lastPageSwitchTime = now;
                    CurrentPage = 1;
                    BuildVisibleItems();
                    return;
                }
                else if (closest.NavigationType == "back")
                {
                    _lastPageSwitchTime = now;
                    CurrentPage = 0;
                    BuildVisibleItems();
                    return;
                }
            }

            // Don't highlight during cooldown after page switch
            if ((DateTime.UtcNow - _lastPageSwitchTime).TotalMilliseconds < 250)
                return;

            HighlightItem(closest);
            UpdateSubmenuDwell(closest);
        }
        else
        {
            HighlightItem(null);
            UpdateSubmenuDwell(null);
        }
    }

    private void HighlightItem(RadialMenuItem? item)
    {
        if (_currentHighlightedItem != item)
        {
            if (_currentHighlightedItem != null) _currentHighlightedItem.IsHighlighted = false;
            _currentHighlightedItem = item;
            
            if (_currentHighlightedItem != null)
            {
                _currentHighlightedItem.IsHighlighted = true;

                // Update wedge — item menu dalam pakai wedge radius dalam;
                // segmen cincin luar (TabId terisi) pakai wedge radius luar.
                WedgeStart = _currentHighlightedItem.WedgeStartAngle;
                WedgeSweep = _currentHighlightedItem.WedgeSweepAngle;
                IsWedgeVisible = string.IsNullOrEmpty(_currentHighlightedItem.TabId);

                
                // Update label
                HighlightLabel = _currentHighlightedItem.FullName;
                HighlightLabelX = _currentHighlightedItem.LabelX;
                HighlightLabelY = _currentHighlightedItem.LabelY;
                IsLabelOnLeft = _currentHighlightedItem.LabelSide == "Left";
                
                // Update Center Text
                CenterTextTitle = _currentHighlightedItem.FullName.ToUpper();
                CenterTextSubtitle = "Select";
            }
            else
            {
                IsWedgeVisible = false;

                HighlightLabel = "";
                CenterTextTitle = "";
                CenterTextSubtitle = "";
            }
            
            OnPropertyChanged(nameof(HighlightedItem)); 
        }
    }

    /// <summary>
        /// Reset to page 1 when menu is shown
        /// </summary>
        public void ResetPage()
        {
            ClearSubmenu();
            CurrentPage = 0;
            BuildVisibleItems();
        }

        // ============================================================
        // Submenu tab DOC (SEND-TEXT / SEND-LOGO)
        // ============================================================

        /// <summary>
        /// Kelola timer dwell: hover item ber-HasSubmenu (di luar submenu)
        /// selama SubmenuDwellMs membuka submenu pilihan tab DOC.
        /// </summary>
        private void UpdateSubmenuDwell(RadialMenuItem? item)
        {
            bool canDwell = item != null
                && item.HasSubmenu
                && !item.IsNavigation;

            if (!canDwell)
            {
                StopSubmenuDwell();
                return;
            }

            if (ReferenceEquals(_dwellCandidate, item)) return; // sudah berjalan

            _dwellCandidate = item;
            _submenuDwellTimer?.Stop();
            _submenuDwellTimer ??= new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SubmenuDwellMs)
            };
            _submenuDwellTimer.Tick -= OnSubmenuDwellTick;
            _submenuDwellTimer.Tick += OnSubmenuDwellTick;
            _submenuDwellTimer.Start();
        }

        private void StopSubmenuDwell()
        {
            _submenuDwellTimer?.Stop();
            _dwellCandidate = null;
        }

        private void OnSubmenuDwellTick(object? sender, EventArgs e)
        {
            _submenuDwellTimer?.Stop();
            var candidate = _dwellCandidate;
            _dwellCandidate = null;
            if (candidate == null) return;
            if (!IsVisible) return;
            TryEnterSubmenu(candidate);
        }

        /// <summary>
        /// Ambil daftar tab DOC lewat BatchViewModel (fresh dari memori).
        /// null = BatchViewModel belum ada / tidak membalas → submenu tidak muncul,
        /// perilaku lama (kirim pakai tab aktif) tetap berlaku.
        /// </summary>
        private List<Messages.DocTabSnapshot>? QueryDocTabs()
        {
            try
            {
                var msg = new Messages.GetDocTabsRequestMessage();
                WeakReferenceMessenger.Default.Send(msg);
                if (msg.HasReceivedResponse)
                    return msg.Response.ToList();
            }
            catch (Exception ex)
            {
                _logService?.AddLog($"[WARNING] Gagal mengambil daftar tab DOC: {ex.Message}");
            }
            return null;
        }

        private void TryEnterSubmenu(RadialMenuItem item)
        {
            if (!item.HasSubmenu || item.IsNavigation) return;

            // Sudah terbuka untuk item ini -> tidak perlu query ulang.
            if (SubmenuItems.Count > 0 && item.ScriptPath == _submenuScriptPath) return;

            var tabs = QueryDocTabs();
            // Hanya tampilkan cincin luar kalau ada > 1 tab DOC (kasus user).
            if (tabs == null || tabs.Count <= 1)
            {
                ClearSubmenu();
                return;
            }

            _submenuTabs = tabs;
            _submenuScriptPath = item.ScriptPath;
            BuildOuterSubmenu(item.Angle);
        }

        /// <summary>
        /// Mintai BatchViewModel menulis doc_info.json milik tab terpilih
        /// sebelum script SEND-TEXT / SEND-LOGO dibaca Photoshop.
        /// </summary>
        private bool PrepareDocSend(string tabId)
        {
            try
            {
                var msg = new Messages.PrepareDocSendRequestMessage(tabId);
                WeakReferenceMessenger.Default.Send(msg);
                if (!msg.HasReceivedResponse)
                {
                    // BatchViewModel belum ada → jalan dengan doc_info.json apa adanya.
                    return false;
                }
                return msg.Response;
            }
            catch (Exception ex)
            {
                _logService?.AddLog($"[WARNING] Gagal menyiapkan data tab DOC: {ex.Message}");
                return false;
            }
        }

        public void ExecuteHighlighted()
        {
            if (_currentHighlightedItem != null && !_currentHighlightedItem.IsNavigation)
            {
                ExecuteItem(_currentHighlightedItem);
            }
            else
            {
                // Missed selection -> Close
                RequestClose?.Invoke();
            }
        }

        /// <summary>
        /// Jalankan satu item menu — dipakai juga hook lepas-tombol dan klik
        /// langsung tombol XAML. Item cincin luar (TabId terisi) menyiapkan
        /// doc_info.json milik tab tsb lebih dulu.
        /// </summary>
        [RelayCommand]
        private void ExecuteItem(RadialMenuItem? item)
        {
            if (item == null || item.IsNavigation) return;
            if (!string.IsNullOrEmpty(item.TabId) && !PrepareDocSend(item.TabId))
            {
                _logService?.AddLog("[WARNING] Data tab DOC tidak siap; script jalan dengan doc_info.json saat ini.");
            }
            _ = ExecuteScript(item.ScriptPath);
        }

    // ============================================================
    // Script Loading & Metadata (unchanged from original)
    // ============================================================

    private Dictionary<string, ScriptConfig> _scriptAliases = new();

    private void AddItem(ObservableCollection<RadialMenuItem> collection, string path, bool isMaster)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("_")) return;

        string displayName;
        string shortName;
        int order = 9999;

        Avalonia.Media.StreamGeometry? icon = null;

        if (_scriptAliases.ContainsKey(fileName))
        {
            var config = _scriptAliases[fileName];
            displayName = config.Name;
            order = config.Order;
            
            if (!string.IsNullOrEmpty(config.IconKey))
            {
                 if (Application.Current!.TryGetResource(config.IconKey, null, out var res) && res is Avalonia.Media.StreamGeometry geom)
                 {
                     icon = geom;
                 }
            }
            
            if (!string.IsNullOrEmpty(config.Code))
            {
                shortName = config.Code;
            }
            else
            {
                shortName = RadialMenuItem.GenerateShortName(displayName);
            }
        }
        else
        {
            displayName = Path.GetFileNameWithoutExtension(fileName);
            shortName = RadialMenuItem.GenerateShortName(displayName);
        }

        collection.Add(new RadialMenuItem
                {
                    FullName = displayName,
                    ShortName = shortName,
                    ScriptPath = path,
                    IsMaster = isMaster,
                    Order = order,
                    Icon = icon,
                    HasSubmenu = IsDocActionScript(fileName, displayName)
                });
    }

    private void LoadMetadata()
    {
        try
        {
            var metaPath = Path.Combine(BMachine.Core.Platform.PlatformServiceFactory.Get().GetAppDataDirectory(), "scripts.json");
            
            var folder = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            
            if (!File.Exists(metaPath))
            {
                var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "scripts.json");
                if (File.Exists(defaultPath))
                {
                    File.Copy(defaultPath, metaPath);
                }
            }

            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                try 
                {
                    _scriptAliases = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScriptConfig>>(json) ?? new();
                }
                catch
                {
                    _scriptAliases = new(); 
                }
            }
        }
        catch { _scriptAliases = new(); }
    }

    [RelayCommand]
    public async Task ExecuteScript(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            _logService?.AddLog($"[INFO] Menjalankan Action Script: {fileName}");

            var context = new { 
                SourceFolders = new System.Collections.Generic.List<object>(),
                OutputBasePath = "",
                UseInput = false,
                UseOutput = false
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(context, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var tempPath = await Services.BmachineContextService.WriteContextAsync(
                json, msg => _logService?.AddLog($"[INFO] Context written to: {msg}"));
            
            _logService?.AddLog($"[INFO] Context written to: {tempPath}");
            
            var ext = Path.GetExtension(path).ToLower();
            
            if (ext == ".jsx")
            {
                 _logService?.AddLog($"[INFO] Launching JSX: {fileName}");
                 
                 var photoshopPath = await _database.GetAsync<string>("Configs.Master.PhotoshopPath");
                 if (string.IsNullOrEmpty(photoshopPath) || !_platformService.IsExecutableValid(photoshopPath))
                 {
                      _logService?.AddLog($"[ERROR] Photoshop path is invalid or not set. Please set it in Settings.\nPath checked: {photoshopPath}");
                      RequestClose?.Invoke();
                      return;
                 }
                 
                 _platformService.RunJsxInPhotoshop(path, photoshopPath);
                 _logService?.AddLog("[SUCCESS] JSX sent to Photoshop.");
            }
            else if (ext == ".pyw")
            {
                 if (fileName.Equals("psdbucin_v3.pyw", StringComparison.OrdinalIgnoreCase))
                 {
                     _logService?.AddLog("[INFO] Launching Native Toolbox Window");
                     Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                     {
                         var window = new Views.ToolboxWindow();
                         window.Show();
                     });
                 }
                 else
                 {
                     _logService?.AddLog($"[INFO] Launching Python Script: {fileName}");
                     _platformService.RunPythonScript(path, true);
                     _logService?.AddLog("[SUCCESS] Python script launched.");
                 }
            }
            else
            {
                 _logService?.AddLog($"[INFO] Launching Script: {fileName}");
                 _platformService.RunPythonScript(path, false);
                 _logService?.AddLog("[SUCCESS] Script launched.");
            }
             
             RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logService?.AddLog($"[ERROR] Action Launch Failed: {ex.Message}");
        }
    }

    public event Action? RequestClose;
}
