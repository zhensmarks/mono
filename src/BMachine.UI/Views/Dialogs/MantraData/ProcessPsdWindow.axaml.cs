using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class ProcessPsdWindow : Window
{
    public string PsdFolder { get; private set; } = string.Empty;
    public string PhotoFolder { get; private set; } = string.Empty;
    public bool Confirmed { get; private set; } = false;

    private readonly IBrush _defaultBadgeBg = SolidColorBrush.Parse("#21262D");
    private readonly IBrush _defaultBadgeText = SolidColorBrush.Parse("#8B949E");
    private readonly IBrush _successBadgeBg = SolidColorBrush.Parse("#0D2818");
    private readonly IBrush _successBadgeText = SolidColorBrush.Parse("#3FB950");
    private readonly IBrush _successBorder = SolidColorBrush.Parse("#2EA043");
    private readonly IBrush _defaultBorder = SolidColorBrush.Parse("#30363D");

    public ProcessPsdWindow()
    {
        InitializeComponent();
    }

    public ProcessPsdWindow(string initialPsdFolder, string initialPhotoFolder) : this()
    {
        if (!string.IsNullOrEmpty(initialPsdFolder) && Directory.Exists(initialPsdFolder))
            PsdFolder = initialPsdFolder;

        if (!string.IsNullOrEmpty(initialPhotoFolder) && Directory.Exists(initialPhotoFolder))
            PhotoFolder = initialPhotoFolder;

        UpdatePsdUi();
        UpdatePhotoUi();
        UpdateOverallSummary();
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
            TxtPsdFolderName.Text = "Pilih folder template layout PSD";
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
            TxtPhotoFolderName.Text = "Pilih folder foto siswa atau guru";
            TxtPhotoFolderName.Foreground = SolidColorBrush.Parse("#C9D1D9");
            TxtPhotoPath.Text = "Mendukung format gambar .jpg, .jpeg, .png, .psd";
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

    private void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
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
