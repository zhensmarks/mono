using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class ProcessPsdWindow : MantraDialogBase
{
    public string PsdFolder { get; private set; } = string.Empty;
    public string PhotoFolder { get; private set; } = string.Empty;
    public bool Confirmed { get; private set; } = false;
    public bool IsRevision { get; private set; } = false;
    public List<string> RevisionFields { get; private set; } = new();

    private readonly List<CheckBox> _revisionFieldBoxes = new();

    private readonly IBrush _defaultBadgeBg = SolidColorBrush.Parse("#21262D");
    private readonly IBrush _defaultBadgeText = SolidColorBrush.Parse("#8B949E");
    private readonly IBrush _successBadgeBg = SolidColorBrush.Parse("#0D2818");
    private readonly IBrush _successBadgeText = SolidColorBrush.Parse("#3FB950");
    private readonly IBrush _successBorder = SolidColorBrush.Parse("#2EA043");
    private readonly IBrush _defaultBorder = SolidColorBrush.Parse("#30363D");
    private readonly IBrush _dragBorder = SolidColorBrush.Parse("#388BFD");

    public ProcessPsdWindow()
    {
        InitializeComponent();
    }

    public ProcessPsdWindow(string initialPsdFolder, string initialPhotoFolder, IEnumerable<string>? columnHeaders = null) : this()
    {
        if (!string.IsNullOrEmpty(initialPsdFolder) && Directory.Exists(initialPsdFolder))
            PsdFolder = initialPsdFolder;

        if (!string.IsNullOrEmpty(initialPhotoFolder) && Directory.Exists(initialPhotoFolder))
            PhotoFolder = initialPhotoFolder;

        if (columnHeaders != null)
        {
            foreach (var header in columnHeaders.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                var box = new CheckBox
                {
                    Content = header,
                    FontSize = 11.5,
                    Foreground = SolidColorBrush.Parse("#C9D1D9"),
                    Margin = new Avalonia.Thickness(0, 0, 12, 6)
                };
                box.IsCheckedChanged += (_, _) => UpdateRevisionUi();
                _revisionFieldBoxes.Add(box);
                RevFieldsWrap.Children.Add(box);
            }
        }

        UpdatePsdUi();
        UpdatePhotoUi();
        UpdateRevisionUi();
    }

    private void UpdatePsdUi()
    {
        if (Directory.Exists(PsdFolder))
        {
            try
            {
                int count = Directory.EnumerateFiles(PsdFolder, "*.psd", SearchOption.AllDirectories).Count();
                var dirName = Path.GetFileName(PsdFolder);
                if (string.IsNullOrEmpty(dirName)) dirName = PsdFolder;

                TxtPsdFolderName.Text = dirName;
                TxtPsdFolderName.Foreground = SolidColorBrush.Parse("#F0F6FC");
                TxtPsdPath.Text = PsdFolder;
                TxtPsdPath.Foreground = SolidColorBrush.Parse("#8B949E");

                BadgePsd.Background = _successBadgeBg;
                TxtBadgePsd.Text = $"{count} File PSD";
                TxtBadgePsd.Foreground = _successBadgeText;

                DropZonePsd.BorderBrush = _successBorder;
                BtnClearPsd.IsVisible = true;

                PillPsdReady.Background = _successBadgeBg;
                TxtPillPsd.Text = $"PSD: {count} File";
                TxtPillPsd.Foreground = _successBadgeText;
            }
            catch (Exception ex)
            {
                TxtPsdPath.Text = $"Error: {ex.Message}";
            }
        }
        else
        {
            TxtPsdFolderName.Text = "Tarik & lepas folder PSD ke sini atau klik untuk browse";
            TxtPsdFolderName.Foreground = SolidColorBrush.Parse("#C9D1D9");
            TxtPsdPath.Text = "Mendukung folder berisikan file dokumen .psd";
            TxtPsdPath.Foreground = SolidColorBrush.Parse("#8B949E");

            BadgePsd.Background = _defaultBadgeBg;
            TxtBadgePsd.Text = "Belum Dipilih";
            TxtBadgePsd.Foreground = _defaultBadgeText;

            DropZonePsd.BorderBrush = _defaultBorder;
            BtnClearPsd.IsVisible = false;

            PillPsdReady.Background = _defaultBadgeBg;
            TxtPillPsd.Text = "PSD: -";
            TxtPillPsd.Foreground = _defaultBadgeText;
        }
    }

    private void UpdatePhotoUi()
    {
        if (Directory.Exists(PhotoFolder))
        {
            try
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".psd" };
                int count = Directory.EnumerateFiles(PhotoFolder, "*.*", SearchOption.AllDirectories)
                    .Count(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                var dirName = Path.GetFileName(PhotoFolder);
                if (string.IsNullOrEmpty(dirName)) dirName = PhotoFolder;

                TxtPhotoFolderName.Text = dirName;
                TxtPhotoFolderName.Foreground = SolidColorBrush.Parse("#F0F6FC");
                TxtPhotoPath.Text = PhotoFolder;
                TxtPhotoPath.Foreground = SolidColorBrush.Parse("#8B949E");

                BadgePhoto.Background = _successBadgeBg;
                TxtBadgePhoto.Text = $"{count} Foto";
                TxtBadgePhoto.Foreground = _successBadgeText;

                DropZonePhoto.BorderBrush = _successBorder;
                BtnClearPhoto.IsVisible = true;

                PillPhotoReady.Background = _successBadgeBg;
                TxtPillPhoto.Text = $"Foto: {count} File";
                TxtPillPhoto.Foreground = _successBadgeText;
            }
            catch (Exception ex)
            {
                TxtPhotoPath.Text = $"Error: {ex.Message}";
            }
        }
        else
        {
            TxtPhotoFolderName.Text = "Tarik & lepas folder foto ke sini atau klik untuk browse";
            TxtPhotoFolderName.Foreground = SolidColorBrush.Parse("#C9D1D9");
            TxtPhotoPath.Text = "Mendukung file foto format .png, .jpg, .jpeg, atau .psd";
            TxtPhotoPath.Foreground = SolidColorBrush.Parse("#8B949E");

            BadgePhoto.Background = _defaultBadgeBg;
            TxtBadgePhoto.Text = "Belum Dipilih";
            TxtBadgePhoto.Foreground = _defaultBadgeText;

            DropZonePhoto.BorderBrush = _defaultBorder;
            BtnClearPhoto.IsVisible = false;

            PillPhotoReady.Background = _defaultBadgeBg;
            TxtPillPhoto.Text = "Foto: -";
            TxtPillPhoto.Foreground = _defaultBadgeText;
        }
    }

    private void UpdateOverallSummary()
    {
        bool psdOk = Directory.Exists(PsdFolder);
        bool photoOk = Directory.Exists(PhotoFolder);
        bool isRev = ChkRevision.IsChecked == true;
        int selected = _revisionFieldBoxes.Count(b => b.IsChecked == true);

        if (isRev)
        {
            if (psdOk && selected > 0)
            {
                TxtSummaryMessage.Text = $"Revisi siap. {selected} kolom akan diperbarui tanpa ganti foto.";
                TxtSummaryMessage.Foreground = _successBadgeText;
                BtnSubmit.IsEnabled = true;
            }
            else if (psdOk)
            {
                TxtSummaryMessage.Text = "Centang minimal satu kolom untuk diperbarui.";
                TxtSummaryMessage.Foreground = SolidColorBrush.Parse("#FBBF24");
                BtnSubmit.IsEnabled = false;
            }
            else
            {
                TxtSummaryMessage.Text = "Pilih folder template PSD untuk merevisi.";
                TxtSummaryMessage.Foreground = _defaultBadgeText;
                BtnSubmit.IsEnabled = false;
            }
            return;
        }

        if (psdOk && photoOk)
        {
            TxtSummaryMessage.Text = "Siap diproses. Folder PSD dan Foto valid.";
            TxtSummaryMessage.Foreground = _successBadgeText;
            BtnSubmit.IsEnabled = true;
        }
        else if (psdOk)
        {
            TxtSummaryMessage.Text = "Pilih folder foto untuk melanjutkan.";
            TxtSummaryMessage.Foreground = SolidColorBrush.Parse("#FBBF24");
            BtnSubmit.IsEnabled = false;
        }
        else if (photoOk)
        {
            TxtSummaryMessage.Text = "Pilih folder template PSD untuk melanjutkan.";
            TxtSummaryMessage.Foreground = SolidColorBrush.Parse("#FBBF24");
            BtnSubmit.IsEnabled = false;
        }
        else
        {
            TxtSummaryMessage.Text = "Menunggu pemilihan folder...";
            TxtSummaryMessage.Foreground = _defaultBadgeText;
            BtnSubmit.IsEnabled = false;
        }
    }

    private async void OnBrowsePsdClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Layout PSD",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            PsdFolder = folders[0].Path.LocalPath;
            UpdatePsdUi();
            UpdateOverallSummary();
        }
    }

    private async void OnBrowsePhotoClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih Folder Foto Siswa / Guru",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            PhotoFolder = folders[0].Path.LocalPath;
            UpdatePhotoUi();
            UpdateOverallSummary();
        }
    }

    private void OnClearPsdClick(object? sender, RoutedEventArgs e)
    {
        PsdFolder = string.Empty;
        UpdatePsdUi();
        UpdateOverallSummary();
    }

    private void OnClearPhotoClick(object? sender, RoutedEventArgs e)
    {
        PhotoFolder = string.Empty;
        UpdatePhotoUi();
        UpdateOverallSummary();
    }

    private void OnRevisionModeChanged(object? sender, RoutedEventArgs e) => UpdateRevisionUi();

    private void OnClearRevisionFieldsClick(object? sender, RoutedEventArgs e)
    {
        foreach (var box in _revisionFieldBoxes) box.IsChecked = false;
        UpdateRevisionUi();
    }

    private void UpdateRevisionUi()
    {
        bool isRev = ChkRevision.IsChecked == true;
        IsRevision = isRev;
        PanelRevision.IsVisible = isRev;
        DropZonePhoto.IsEnabled = !isRev;
        DropZonePhoto.Opacity = isRev ? 0.55 : 1.0;

        if (isRev)
        {
            TxtPhotoFolderName.Text = "Opsional dalam mode revisi — foto tidak dimasukkan ulang";
            TxtPhotoFolderName.Foreground = SolidColorBrush.Parse("#C9D1D9");
            TxtPhotoPath.Text = "Biarkan pilihan lama atau kosongkan";
            TxtPhotoPath.Foreground = SolidColorBrush.Parse("#8B949E");
            DropZonePhoto.BorderBrush = _defaultBorder;
        }
        else
        {
            UpdatePhotoUi();
        }

        UpdateOverallSummary();
    }

    private void OnPsdDragEnter(object? sender, DragEventArgs e) => HandleDragEnter(DropZonePsd, e);
    private void OnPsdDragOver(object? sender, DragEventArgs e) => HandleDragOver(e);
    private void OnPsdDragLeave(object? sender, DragEventArgs e) => UpdatePsdUi();

    private void OnPsdDrop(object? sender, DragEventArgs e)
    {
        var folder = ExtractFolderPathFromDrop(e);
        if (!string.IsNullOrEmpty(folder))
        {
            PsdFolder = folder;
        }
        UpdatePsdUi();
        UpdateOverallSummary();
    }

    private void OnPhotoDragEnter(object? sender, DragEventArgs e) => HandleDragEnter(DropZonePhoto, e);
    private void OnPhotoDragOver(object? sender, DragEventArgs e) => HandleDragOver(e);
    private void OnPhotoDragLeave(object? sender, DragEventArgs e) => UpdatePhotoUi();

    private void OnPhotoDrop(object? sender, DragEventArgs e)
    {
        var folder = ExtractFolderPathFromDrop(e);
        if (!string.IsNullOrEmpty(folder))
        {
            PhotoFolder = folder;
        }
        UpdatePhotoUi();
        UpdateOverallSummary();
    }

    private void HandleDragEnter(Border zone, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            zone.BorderBrush = _dragBorder;
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private static void HandleDragOver(DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static string? ExtractFolderPathFromDrop(DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0) return null;

        var first = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(first)) return null;
        if (Directory.Exists(first)) return first;
        if (File.Exists(first)) return Path.GetDirectoryName(first);
        return null;
    }

    private void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        bool isRev = ChkRevision.IsChecked == true;

        if (isRev)
        {
            if (!Directory.Exists(PsdFolder)) return;
            RevisionFields = _revisionFieldBoxes
                .Where(b => b.IsChecked == true)
                .Select(b => (string)(b.Content ?? ""))
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();
            if (RevisionFields.Count == 0) return;
            IsRevision = true;
            Confirmed = true;
            Close(true);
            return;
        }

        if (Directory.Exists(PsdFolder) && Directory.Exists(PhotoFolder))
        {
            Confirmed = true;
            Close(true);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}
