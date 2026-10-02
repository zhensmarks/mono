using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PixelcutCompact.Services.Editing;
using IOPath = System.IO.Path;

namespace PixelcutCompact.Views;

public partial class PreviewWindow
{
    // ========================
    // SAVE
    // ========================

    private void OnSaveEditClick(object? sender, RoutedEventArgs e) => SaveInPlace();

    private bool SaveInPlace()
    {
        if (_session == null) return false;
        if (_session.HasPendingRestore && !_session.HasRestoreSource)
        {
            Toast(T("Toast_OriginalLoading"));
            return false;
        }

        try
        {
            // Ctrl+S langsung me-replace file asli; tidak ada backup .bak.
            _session.BakeToFile(_resultPath);
            _session.MarkSaved();

            Toast(T("Toast_Saved"));
            Saved?.Invoke(this, _resultPath);
            UpdateEditorStatus();
            return true;
        }
        catch (Exception ex)
        {
            Toast(T("Toast_FailSave", ex.Message));
            return false;
        }
    }

    private async void OnSaveAsEditClick(object? sender, RoutedEventArgs e)
        => await SaveAsCopyInteractively();

    /// <summary>
    /// Photoshop (F12 = Revert): buang SEMUA perubahan sesi edit dan muat ulang
    /// gambar dari file di disk. Dipakai tombol toolbar "Kembalikan" dan F12.
    /// Bila sesi kotor, user diminta konfirmasi dulu.
    /// </summary>
    private async void OnRevertEditClick(object? sender, RoutedEventArgs e)
    {
        if (_session == null || !_editMode) return;
        if (!_session.IsDirty)
        {
            Toast(T("Toast_NothingToRevert"));
            return;
        }
        if (!await AskDiscardConfirmAsync()) return;
        await DiscardEditsAsync();
    }

    /// <summary>Dialog konfirmasi kecil: buang perubahan atau batal.</summary>
    /// <returns>true bila user memilih Buang.</returns>
    private async Task<bool> AskDiscardConfirmAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = T("Dlg_DiscardTitle"),
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B"))
        };

        var discard = new Button
        {
            Content = T("Btn_Discard"), Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#B91C1C")),
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand)
        };
        var cancel = new Button
        {
            Content = T("Btn_Cancel"), Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand)
        };

        discard.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = T("Dlg_DiscardHead"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")) },
                new TextBlock { Text = T("Dlg_DiscardBody"), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")), TextWrapping = TextWrapping.Wrap },
                // Tombol vertikal full-width seragam, ala referensi Photoshop.
                new StackPanel
                {
                    Spacing = 6,
                    Children = { discard, cancel }
                }
            }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    /// <summary>
    /// Buang sesi edit saat ini dan buat ulang dari file di disk
    /// (<see cref="_resultPath"/>), lalu segarkan seluruh tampilan editor.
    /// Dipakai oleh "Buang perubahan" (dialog pindah gambar) dan Revert (F12/toolbar).
    /// </summary>
    /// <returns>true bila sesi berhasil dimuat ulang.</returns>
    private async Task<bool> DiscardEditsAsync()
    {
        if (_session == null) return false;
        bool wasEditing = _editMode;
        try
        {
            // PrepareEditorAsync: keluar mode edit diam-diam, dispose sesi lama,
            // decode ulang file dari disk, dan buat sesi baru yang bersih.
            if (!await PrepareEditorAsync(_originalPath, _resultPath)) return false;
            if (_session == null) return false;

            // Gambar ulang seluruh pratinjau dari sesi yang baru.
            _resultDirtyBounds = PixelBounds.Full(_session.Width, _session.Height);
            RefreshResultBitmap();
            RenderAnts();
            RenderOverlay();
            RenderQuickMask();
            UpdateEditorStatus();
            UpdateRestoreAvailability();

            if (wasEditing) EnterEditMode();
            Toast(T("Toast_Discarded"));
            return true;
        }
        catch (Exception ex)
        {
            Toast(T("Toast_FailRevert", ex.Message), warning: true);
            return false;
        }
    }

    /// <summary>Versi fire-and-forget untuk shortcut keyboard (F12).</summary>
    private async Task RevertEditsAsync()
    {
        if (_session == null || !_editMode) return;
        if (!_session.IsDirty)
        {
            Toast(T("Toast_NothingToRevert"));
            return;
        }
        if (!await AskDiscardConfirmAsync()) return;
        await DiscardEditsAsync();
    }

    /// <summary>
    /// Tanya user apakah perubahan belum disimpan ditulis dulu (Simpan / Simpan Salinan),
    /// dibuang (Buang perubahan), atau dibatalkan (Batal).
    /// </summary>
    /// <returns>
    /// true  = perubahan sudah disimpan in-place atau dibuang (aman lanjut tanpa dialog lagi);
    /// false = tulis sebagai salinan, atau user membatalkan → pemanggil lanjut dengan
    ///         pembersihan diri sendiri.
    /// </returns>
    private async Task<bool> ConfirmDiscardChanges()
    {
        if (_session is not { IsDirty: true }) return true;

        var tcs = new TaskCompletionSource<int>(); // 0 batal, 1 simpan in-place, 2 salinan, 3 buang
        var dialog = new Window
        {
            Title = T("Dlg_UnsavedTitle"),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#2B2B2B"))
        };

        Button DlgBtn(string content, string bg) => new()
        {
            Content = content, Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(3),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse(bg)),
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Cursor = new Cursor(StandardCursorType.Hand)
        };
        var save = DlgBtn(T("Btn_Save"), "#3A3A3A");
        var copy = DlgBtn(T("Dlg_SaveCopy"), "#3A3A3A");
        var discard = DlgBtn(T("Btn_Discard"), "#B91C1C");
        var cancel = DlgBtn(T("Btn_Cancel"), "#3A3A3A");
        ToolTip.SetTip(discard, T("Dlg_DiscardBody"));

        save.Click += (_, _) => { tcs.TrySetResult(1); dialog.Close(); };
        copy.Click += (_, _) => { tcs.TrySetResult(2); dialog.Close(); };
        discard.Click += (_, _) => { tcs.TrySetResult(3); dialog.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(0); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(0);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = T("Dlg_SaveMoveHead"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")) },
                new TextBlock { Text = T("Dlg_SaveMoveBody"), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A6A6A6")), TextWrapping = TextWrapping.Wrap },
                // Tombol vertikal full-width seragam, ala referensi Photoshop.
                new StackPanel
                {
                    Spacing = 6,
                    Children = { save, copy, discard, cancel }
                }
            }
        };

        await dialog.ShowDialog(this);
        var choice = await tcs.Task;

        if (choice == 1) return SaveInPlace();
        if (choice == 2) { await SaveAsCopyInteractively(); return false; }
        if (choice == 3) { await DiscardEditsAsync(); return true; }
        return false;
    }

    /// <summary>
    /// Buka file picker "Simpan sebagai" dan tulis hasil edit sebagai salinan.
    /// Dipakai oleh tombol Save As dan oleh guard pindah gambar.
    /// </summary>
    private async Task SaveAsCopyInteractively()
    {
        if (_session == null) return;
        if (_session.HasPendingRestore && !_session.HasRestoreSource)
        {
            Toast(T("Toast_OriginalLoading"));
            return;
        }

        try
        {
            var suggested = IOPath.GetFileNameWithoutExtension(string.IsNullOrEmpty(_resultPath) ? "hasil" : _resultPath)
                            + _settings.EditorOutputSuffix + ".png";

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("Dlg_SaveTitle"),
                SuggestedFileName = suggested,
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } }
                }
            });

            if (file?.TryGetLocalPath() is not { } path || string.IsNullOrEmpty(path)) return;

            _session.BakeToFile(path);
            _session.MarkSaved();
            Toast(T("Toast_SavedCopy"));
            Saved?.Invoke(this, path);
        }
        catch (Exception ex)
        {
            Toast(T("Toast_FailSaveCopy", ex.Message));
        }
    }
}
