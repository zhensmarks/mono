using Avalonia.Controls;
using Avalonia.Interactivity;
using BMachine.UI.ViewModels;

namespace BMachine.UI.Views.Dialogs;

/// <summary>
/// Settings dialog for the DOC tab. Lets the user configure the placeholder
/// ("teks asal") strings that SEND-TEXT.jsx searches for inside the PSD before
/// replacing them with the current Name / Address. This makes the replace logic
/// work with templates whose text differs from the built-in defaults.
/// </summary>
public partial class DocSettingsWindow : Window
{
    private readonly BatchViewModel _batchVm;

    /// <summary>True when the user pressed Simpan (Save).</summary>
    public bool Saved { get; private set; }

    public DocSettingsWindow(BatchViewModel batchVm)
    {
        InitializeComponent();
        _batchVm = batchVm;

        // Prefill with the currently configured sources.
        TxtNameSources.Text = batchVm.NameSources;
        TxtAddressSources.Text = batchVm.AddressSources;
    }

    private void OnResetDefaultClick(object? sender, RoutedEventArgs e)
    {
        TxtNameSources.Text = BatchViewModel.DefaultNameSources;
        TxtAddressSources.Text = BatchViewModel.DefaultAddressSources;
        StatusText.Text = "Daftar dikembalikan ke bawaan. Tekan Simpan untuk menerapkan.";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close(false);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // Persisting via the observable properties triggers SaveDocDataAsync(),
        // which also re-exports doc_info.json consumed by SEND-TEXT.jsx.
        _batchVm.NameSources = TxtNameSources.Text ?? "";
        _batchVm.AddressSources = TxtAddressSources.Text ?? "";

        Saved = true;
        Close(true);
    }

    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close(false);
    }
}
