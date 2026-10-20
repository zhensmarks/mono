using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BMachine.UI.Views;
using BMachine.UI.ViewModels;
using BMachine.UI.Services;
using BMachine.Core.Database;
using BMachine.UI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System; 
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace BMachine.App;

public partial class App : Application, 
    IRecipient<ShutdownMessage>,
    IRecipient<SetRecordingModeMessage>,
    IRecipient<UpdateTriggerConfigMessage>
{
    private Avalonia.Controls.Window? _mainWindow;
    private GlobalInputHookService? _inputHook;
    private RadialMenuWindow? _radialMenuWindow;

    private DatabaseService? _db;
    private ProcessLogService? _logService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("[App] Framework Initialization Started");

        // ATURAN: AutoCompleteBox (kolom pencarian board/list, filter, dsb) harus
        // terbuka dengan SATU KLIK di mana pun di dalam kotaknya — dulu hanya klik
        // pada chevron kecil yang membuka daftar, sehingga klik di tengah kotak
        // terasa "tidak bisa diklik". Berlaku global untuk semua AutoCompleteBox.
        // ATURAN: semua context menu seragam. Semi.Avalonia menempelkan menu bawaan
        // (Cut/Copy/Paste) dengan tema + teks BAHASA MANDARIN ("复制") di setiap
        // TextBox, jadi menu di kotak komentar tampil beda dari menu aplikasi.
        // Di sini menu bawaan itu diganti menu kita sendiri: bahasa Inggris,
        // tema aplikasi (ControlTheme ContextMenu/MenuItem di App.axaml).
        // Teks yang bisa diblok (SelectableTextBlock: deskripsi kartu, log) juga
        // dipasangi menu bawaan Semi (Mandarin). Diganti menu kita: Copy + Select All.
        Control.ContextRequestedEvent.AddClassHandler<SelectableTextBlock>((block, e) =>
        {
            try
            {
                if (e.Handled) return;
                var menu = new ContextMenu();
                var items = new List<object>();
                var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control), IsEnabled = !string.IsNullOrEmpty(block.SelectedText) };
                copy.Click += (_, _) => block.Copy();
                var selAll = new MenuItem { Header = "Select All", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
                selAll.Click += (_, _) => block.SelectAll();
                items.Add(copy);
                items.Add(new Separator());
                items.Add(selAll);
                menu.ItemsSource = items;
                menu.Open(block);
                e.Handled = true;
            }
            catch { }
        }, RoutingStrategies.Tunnel);

        Control.ContextRequestedEvent.AddClassHandler<TextBox>((box, e) =>
        {
            try
            {
                if (e.Handled) return;
                var menu = new ContextMenu();
                var items = new List<object>();
                bool ro = box.IsReadOnly;
                var cut = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control), IsEnabled = !ro };
                cut.Click += (_, _) => box.Cut();
                var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
                copy.Click += (_, _) => box.Copy();
                var paste = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control), IsEnabled = !ro };
                paste.Click += (_, _) => box.Paste();
                var selAll = new MenuItem { Header = "Select All", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
                selAll.Click += (_, _) => box.SelectAll();
                items.Add(cut); items.Add(copy); items.Add(paste);
                items.Add(new Separator());
                items.Add(selAll);
                menu.ItemsSource = items;
                menu.Open(box);
                e.Handled = true;
            }
            catch { }
        }, RoutingStrategies.Tunnel);

        InputElement.PointerPressedEvent.AddClassHandler<AutoCompleteBox>((box, e) =>
        {
            try
            {
                if (!box.IsDropDownOpen && e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
                {
                    box.IsDropDownOpen = true;
                }
            }
            catch { }
        }, RoutingStrategies.Tunnel);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try 
            {
                // 1. Show Splash Screen
                ApplySavedThemeBeforeSplash();
                ApplySavedDensityBeforeSplash();
                var splashVm = new SplashViewModel();
                var splashWindow = new SplashWindow
                {
                    DataContext = splashVm
                };
                desktop.MainWindow = splashWindow;
                splashWindow.Show();

                _ = InitializeAppAsync(desktop, splashWindow, splashVm);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Splash screen initialization failed ({ex.GetType().Name}).");
                // Fallback: Try to show MainWindow directly if Splash fails
                try
                {
                    var mainWindow = new BMachine.App.Views.MainWindow();
                    mainWindow.DataContext = new BMachine.App.ViewModels.MainWindowViewModel(new DatabaseService(), new ProcessLogService());
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                }
                catch(Exception e2)
                {
                     Console.WriteLine($"[CRITICAL] Main-window fallback failed ({e2.GetType().Name}).");
                }
            }
            
            base.OnFrameworkInitializationCompleted();
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplySavedThemeBeforeSplash()
    {
        try
        {
            var themeStr = Task.Run(async () =>
            {
                var db = new DatabaseService();
                return await db.GetAsync<string>("Settings.Theme");
            }).GetAwaiter().GetResult();
            RequestedThemeVariant = themeStr switch
            {
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                "System" => Avalonia.Styling.ThemeVariant.Default,
                _ => Avalonia.Styling.ThemeVariant.Dark
            };
        }
        catch { }
    }
    private void ApplySavedDensityBeforeSplash()
    {
        try
        {
            var value = Task.Run(async () =>
            {
                var db = new DatabaseService();
                return await db.GetAsync<string>(BMachine.UI.Services.UiDensityService.SettingKey);
            }).GetAwaiter().GetResult();
            BMachine.UI.Services.UiDensityService.Apply(BMachine.UI.Services.UiDensityService.Parse(value));
        }
        catch { }
    }
    private void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "BMachine_Startup.log");
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private async Task InitializeAppAsync(IClassicDesktopStyleApplicationLifetime desktop, Window splashWindow, SplashViewModel splashVm)
    {
        Log("InitializeAppAsync Started");
        await Task.Delay(100); 

        try 
        {
            // 2. Initialize Services (Background)
            Log("Creating DatabaseService...");
            _db = new DatabaseService();
            Log("DatabaseService Created.");

            _logService = new ProcessLogService();
            // Isi cache kredensial Trello untuk unduhan media inline (gambar di bubble comment).
            // Tanpa cache ini, DownloadMediaAsync dari UI tanpa creds -> foto pribadi gagal unduh (blank).
            _ = Task.Run(async () =>
            {
                try
                {
                    var k = await _db.GetAsync<string>("Trello.ApiKey");
                    var t = await _db.GetAsync<string>("Trello.Token");
                    BMachine.UI.Services.TrelloRequestSecurity.SetCachedCredentials(k, t);
                }
                catch { /* tanpa cache, unduhan media butuh auth akan gagal (sudah ditangani) */ }
            });

            Log("Creating Bootstrapper...");
            var bootstrapper = new Bootstrapper(_db);
            IProgress<double> progress = new Progress<double>(p => splashVm.Progress = p);
            IProgress<string> status = new Progress<string>(s => 
            {
                splashVm.StatusText = s;
                splashVm.AddLog(s);
                Log($"[Bootstrapper] {s}");
            });

            // Run initialization
            Log("Running Bootstrapper.InitializeAsync...");
            await bootstrapper.InitializeAsync(progress, status);
            Log("Bootstrapper Completed.");
            await Task.Delay(80);

            // 3. Create Main Window
            Log("Creating MainWindow...");
            var mainWindow = new BMachine.App.Views.MainWindow(); 
            Log("Creating MainWindowViewModel...");
            var mainVm = new BMachine.App.ViewModels.MainWindowViewModel(_db, _logService);
            mainWindow.DataContext = mainVm;
            Log("MainWindowViewModel Created.");
            
            // MACOS STARTUP BUG FIX: Pre-load dimensions BEFORE the window is SHOWN!
            // This prevents the OS from clipping the Avalonia layout due to programmatic resizing.
            var saved = await mainVm.GetSavedWindowState();
            if (saved != null)
            {
                bool isOnScreen = false;
                var targetRect = new PixelRect(saved.Value.X, saved.Value.Y, (int)saved.Value.W, (int)saved.Value.H);
                
                foreach(var screen in mainWindow.Screens.All)
                {
                    if (screen.Bounds.Intersects(targetRect))
                    {
                        isOnScreen = true;
                        break;
                    }
                }

                if (isOnScreen)
                {
                    mainWindow.Width = Math.Clamp(saved.Value.W, 800, 3840);
                    mainWindow.Height = Math.Clamp(saved.Value.H, 500, 2160);
                    mainWindow.WindowState = saved.Value.State == WindowState.Maximized
                        ? WindowState.Maximized
                        : WindowState.Normal;
                    mainWindow.Position = new Avalonia.PixelPoint(saved.Value.X, saved.Value.Y);
                    mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                }
                else
                {
                    mainWindow.Width = 1280;
                    mainWindow.Height = 800;
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                mainVm.InitialLogPanelOpen = saved.Value.LogPanel;
            }

            // Wait for Dashboard to initialize while Splash Screen is visible
            Log("Awaiting Dashboard initialization...");
            splashVm.AddLog("Data Pengguna & Dashboard...");
            progress.Report(90);
            
            await mainVm.InitializeDashboardAsync();
            
            splashVm.AddLog("BMachine Siap Beroperasi.");
            progress.Report(100);
            Log("Dashboard is marked ready.");
            
            // Allow progress bar transition animation to finish visually (350ms) before snapping window shut
            await Task.Delay(350);

            // 4. Swap Windows
            Log("Showing MainWindow and closing SplashWindow...");
            desktop.MainWindow = mainWindow; // FIX: Update MainWindow reference
            mainWindow.Show();
            #if DEBUG
            // Dev-only harness (Debug builds only, stripped from publish).
            // BMACHINE_DEV_OPEN=toolbox opens the named window for headless UI capture.
            var devOpen = Environment.GetEnvironmentVariable("BMACHINE_DEV_OPEN");
            if (devOpen == "toolbox")
            {
                new BMachine.UI.Views.ToolboxWindow().Show();
            }
            #endif
            splashWindow.Close();
            Log("MainWindow.Show() called.");
            
            _mainWindow = mainWindow;
            
            // Hook Focus Events
            mainWindow.Activated += (s, e) => WeakReferenceMessenger.Default.Send(new AppFocusChangedMessage(true));
            mainWindow.Deactivated += (s, e) => WeakReferenceMessenger.Default.Send(new AppFocusChangedMessage(false));
            
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            // 5. Post-Init (Hooks)
            try
            {
                Log("Initializing InputHook...");
                _inputHook = new GlobalInputHookService();
                _inputHook.OnMouseWheel += (pos, delX, delY) => {
                    // Normalize delta: Most libs give 1/-1 for notch, but let's pass as is
                    // and let the view decide multiplier. We also use InvalidateVisual to unfreeze.
                    WeakReferenceMessenger.Default.Send(new GlobalMouseWheelMessage(pos, delX, delY));
                };
                _inputHook.OnTriggerDown += OnRadialTrigger;
                _inputHook.OnTriggerUp += OnRadialRelease;
                _inputHook.OnMouseMove += OnRadialMove;
                _inputHook.OnRecorded += OnShortcutRecorded;
                
                LoadInitialShortcutConfig();
                Log("InputHook Initialized.");
            }
            catch(Exception ex)
            {
                Log($"[Hook Error] Global hook initialization failed ({ex.GetType().Name}).");
                _logService.AddLog($"[Hook Error] Global hook initialization failed ({ex.GetType().Name}).");
            }

            desktop.Exit += (s, e) => 
            {
                _radialMenuWindow?.Close();
                _inputHook?.Dispose();
            };
            
            WeakReferenceMessenger.Default.RegisterAll(this);
            Log("Initialization Complete.");
        }
        catch (Exception ex)
        {
             Log($"[CRITICAL ERROR] App launch failed ({ex.GetType().Name}).");
             Console.WriteLine($"[CRITICAL ERROR] App launch failed ({ex.GetType().Name}).");
             splashWindow.Close();
        }
    }
    
    private void OnShortcutRecorded(BMachine.UI.Models.TriggerConfig config)
    {
         // Forward to UI
         WeakReferenceMessenger.Default.Send(new TriggerRecordedMessage(config));
         
         // Auto-disable recording
         if (_inputHook != null) _inputHook.IsRecording = false;
         WeakReferenceMessenger.Default.Send(new SetRecordingModeMessage(false));
    }
    
    public void Receive(SetRecordingModeMessage message)
    {
        if (_inputHook != null)
        {
            _inputHook.IsRecording = message.Value;
        }
    }

    public void Receive(UpdateTriggerConfigMessage message)
    {
        if (_inputHook != null)
        {
            _inputHook.UpdateConfig(message.Value);
        }
    }
    
    private void OnRadialTrigger(Point screenPos)
    {
        Console.WriteLine($"[App] OnRadialTrigger called at {screenPos}");
        
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try 
            {
                if (_radialMenuWindow == null)
                {
                    Console.WriteLine("[App] creating new RadialMenuWindow");
                    _radialMenuWindow = new RadialMenuWindow();
                    var vm = new RadialMenuViewModel(_db, _logService);
                    vm.RequestClose += () => _radialMenuWindow?.Hide();
                    _radialMenuWindow.DataContext = vm;
                    _radialMenuWindow.Closed += (s,e) => _radialMenuWindow = null;
                }
                else
                {
                     Console.WriteLine("[App] Reusing existing RadialMenuWindow");
                }
    
                // Reposition
                double w = _radialMenuWindow.Width;
                double h = _radialMenuWindow.Height;
                if (double.IsNaN(w)) w = 300; 
                if (double.IsNaN(h)) h = 300;
    
                Console.WriteLine($"[App] Positioning at {screenPos.X - w/2}, {screenPos.Y - h/2}");
                var scaling = _radialMenuWindow.Screens.ScreenFromPoint(new PixelPoint((int)screenPos.X, (int)screenPos.Y))?.Scaling ?? 1.0;
                _radialMenuWindow.Position = new PixelPoint((int)Math.Round(screenPos.X - w * scaling / 2), (int)Math.Round(screenPos.Y - h * scaling / 2));
                _radialMenuWindow.Show();
                _radialMenuWindow.Activate(); // Focus
                
                if (_radialMenuWindow.DataContext is RadialMenuViewModel vmRef)
                {
                    vmRef.IsVisible = true;
                    vmRef.ReloadScripts(); 
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Error showing radial menu: {ex}");
            }
        });
    }

    private void OnRadialRelease(Point screenPos)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_radialMenuWindow != null && _radialMenuWindow.IsVisible && _radialMenuWindow.DataContext is RadialMenuViewModel vm)
            {
                vm.ExecuteHighlighted();
                // Window hiding is handled by vm.RequestClose -> _radialMenuWindow.Hide()
            }
        });
    }

    private void OnRadialMove(Point screenPos)
    {
         Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_radialMenuWindow != null && _radialMenuWindow.IsVisible && _radialMenuWindow.DataContext is RadialMenuViewModel vm)
            {
                // Calculate position relative to window top-left
                // Window Position is Top-Left of window in Screen Coords.
                var winPos = _radialMenuWindow.Position;
                double scaling = _radialMenuWindow.RenderScaling;
                Point relPos = new Point((screenPos.X - winPos.X) / scaling, (screenPos.Y - winPos.Y) / scaling);
                vm.UpdateHighlight(relPos, _radialMenuWindow.Bounds.Size);
            }
        });
    }

    public void Receive(ShutdownMessage message)
    {
        _radialMenuWindow?.Close();
        _inputHook?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private async void LoadInitialShortcutConfig()
    {
        try
        {
            if (_db == null) return;
            var json = await _db.GetAsync<string>("ShortcutConfig");
            if (!string.IsNullOrEmpty(json))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<BMachine.UI.Models.TriggerConfig>(json);
                 if (config != null && _inputHook != null)
                 {
                     _inputHook.UpdateConfig(config);
                     Console.WriteLine($"[App] Loaded Initial Shortcut: {config}");
                 }
            }
        }
        catch(Exception ex) { Console.WriteLine($"Error loading shortcut: {ex.Message}"); }
    }
}
