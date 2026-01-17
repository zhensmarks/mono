using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BMachine.UI.Views;
using BMachine.UI.ViewModels;
using BMachine.Core.Database;
using CommunityToolkit.Mvvm.Messaging;
using System; // Added for Exception and Console

namespace BMachine.App;

public partial class App : Application, CommunityToolkit.Mvvm.Messaging.IRecipient<BMachine.UI.Messages.ShutdownMessage>
{
    private Avalonia.Controls.Window? _mainWindow;
    private FloatingWidgetView? _floatingWidget;

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
                var db = new DatabaseService();
                var logService = new BMachine.UI.Services.ProcessLogService(); 

                // 2. Initialize MainWindow with Shared Services
                var mainWindow = new BMachine.App.Views.MainWindow();
                mainWindow.DataContext = new BMachine.App.ViewModels.MainWindowViewModel(db, logService);
                desktop.MainWindow = mainWindow;
                _mainWindow = mainWindow;
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

                // 3. Initialize Theme
                try
                {
                    var themeService = new BMachine.UI.Services.ThemeService(db);
                    themeService.InitializeAsync().Wait(); 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing theme: {ex.Message}");
                }

                // 4. Initialize Floating Widget with Shared Services
                _floatingWidget = new FloatingWidgetView();
                _floatingWidget.DataContext = new FloatingWidgetViewModel(db, HandleFloatingAction, logService);
                _floatingWidget.Show();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching App: {ex.Message}");
            }

            
            desktop.Exit += (s, e) => _floatingWidget?.Close();
            
            // Register as Recipient
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.RegisterAll(this);
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    public void Receive(BMachine.UI.Messages.ShutdownMessage message)
    {
        _floatingWidget?.Close();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
    
    private void HandleFloatingAction(string action)
    {
        if (action == "Home")
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.Activate();
                if (_mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            }
        }
        else if (action == "UploadGDrive")
        {
             Console.WriteLine("Launching Upload GDrive Window...");
        }
        else if (action == "Pixelcut")
        {
             Console.WriteLine("Launching Pixelcut Window...");
        }
    }
}
