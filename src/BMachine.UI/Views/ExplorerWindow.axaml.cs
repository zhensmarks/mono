using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;

namespace BMachine.UI.Views;

public partial class ExplorerWindow : Window
{
    public ExplorerWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
    }

    private void OnDragZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private BMachine.SDK.IDatabase? _database;
    private const string SETTING_KEY_WIDTH = "ExplorerWindow_Width";
    private const string SETTING_KEY_HEIGHT = "ExplorerWindow_Height";

    public void Init(BMachine.SDK.IDatabase database)
    {
        _database = database;
        LoadWindowSize();
    }

    private async void LoadWindowSize()
    {
        if (_database == null) return;
        
        try
        {
            var w = await _database.GetAsync<string>(SETTING_KEY_WIDTH);
            var h = await _database.GetAsync<string>(SETTING_KEY_HEIGHT);

            if (double.TryParse(w, out double width) && width > 100) this.Width = width;
            if (double.TryParse(h, out double height) && height > 100) this.Height = height;
        }
        catch { }
    }

    private async void SaveWindowSize()
    {
        if (_database == null) return;
        try
        {
            await _database.SetAsync<string>(SETTING_KEY_WIDTH, this.Bounds.Width.ToString());
            await _database.SetAsync<string>(SETTING_KEY_HEIGHT, this.Bounds.Height.ToString());
        }
        catch { }
    }

    private void OnCloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveWindowSize();
        this.Close();
    }
    
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SaveWindowSize();
    }
}
