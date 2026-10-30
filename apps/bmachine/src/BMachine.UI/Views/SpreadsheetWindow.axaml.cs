using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BMachine.UI.Views;

public partial class SpreadsheetWindow : Window
{
    private bool _allowClosingAfterDiscard;
    private bool _discardPromptActive;

    public SpreadsheetWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is BMachine.UI.ViewModels.SpreadsheetViewModel vm && vm.IsLoading)
        {
            e.Cancel = true;
            vm.StatusText = "Wait for saving or loading to finish before closing.";
            base.OnClosing(e);
            return;
        }

        if (!_allowClosingAfterDiscard && DataContext is BMachine.UI.ViewModels.SpreadsheetViewModel vmWithChanges && vmWithChanges.HasChanges)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (!_discardPromptActive)
            {
                _discardPromptActive = true;
                _ = ConfirmDiscardUnsavedChangesAsync();
            }
            return;
        }

        if (DataContext is BMachine.UI.ViewModels.SpreadsheetViewModel spreadsheetVM)
        {
            _ = spreadsheetVM.SaveColumnWidthsAsync();
            // Draft disimpan juga saat menutup, jadi isian tidak hilang walaupun belum di-push.
            _ = spreadsheetVM.PersistDraftsAsync();
        }

        base.OnClosing(e);
    }

    private async Task ConfirmDiscardUnsavedChangesAsync()
    {
        try
        {
            var discard = await AskToDiscardUnsavedChangesAsync();
            if (discard)
            {
                _allowClosingAfterDiscard = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spreadsheet close confirmation failed: {ex.GetType().Name}");
        }
        finally
        {
            _discardPromptActive = false;
        }
    }

    protected virtual Task<bool> AskToDiscardUnsavedChangesAsync()
    {
        var dialog = new Dialogs.MantraData.MantraConfirmDialog(
            "Belum di-push ke spreadsheet",
            "Ada perubahan yang belum dikirim ke spreadsheet. Perubahan ini tetap tersimpan "
            + "sebagai draft dan akan muncul lagi saat window dibuka. Tutup sekarang?");
        return dialog.ShowDialog<bool>(this);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
