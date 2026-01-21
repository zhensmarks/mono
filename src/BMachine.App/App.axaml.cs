using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BMachine.UI.Views;
using BMachine.UI.ViewModels;
using BMachine.UI.Services;
using BMachine.Core.Database;
using BMachine.UI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System; 

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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try 
            {
                // 1. Create Shared Services
                _db = new DatabaseService();
                _logService = new ProcessLogService(); 

                // 2. Initialize MainWindow with Shared Services
                var mainWindow = new BMachine.App.Views.MainWindow();
                mainWindow.DataContext = new BMachine.App.ViewModels.MainWindowViewModel(_db, _logService);
                desktop.MainWindow = mainWindow;
                _mainWindow = mainWindow;
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

                // 3. Initialize Theme
                try
                {
                    var themeService = new ThemeService(_db);
                    themeService.InitializeAsync().Wait(); 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing theme: {ex.Message}");
                }

                // 4. Initialize Radial Menu Hook
                try
                {
                    _inputHook = new GlobalInputHookService();
                    _inputHook.OnTriggerDown += OnRadialTrigger;
                    _inputHook.OnTriggerUp += OnRadialRelease;
                    _inputHook.OnMouseMove += OnRadialMove;
                    _inputHook.OnRecorded += OnShortcutRecorded;
                    
                    // Load saved config if exists
                    LoadInitialShortcutConfig();
                    // For now default is Shift+MiddleMouse
                }
                catch(Exception ex)
                {
                    _logService.AddLog($"[Hook Error] Failed to init global hook: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching App: {ex.Message}");
            }

            
            desktop.Exit += (s, e) => 
            {
                _radialMenuWindow?.Close();
                _inputHook?.Dispose();
            };
            
            // Register as Recipient
            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        base.OnFrameworkInitializationCompleted();
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
                _radialMenuWindow.Position = new PixelPoint((int)(screenPos.X - w/2), (int)(screenPos.Y - h/2));
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
                Point relPos = new Point(screenPos.X - winPos.X, screenPos.Y - winPos.Y);
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
