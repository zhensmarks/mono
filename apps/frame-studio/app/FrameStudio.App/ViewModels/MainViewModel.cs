using FrameStudio.App.Models;
using FrameStudio.App.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrameStudio.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IPsdTemplateAdapter _psdAdapter;
    private string _masterPath = "";
    private string _sourcePath = "";
    private string _outputPath = "";
    private string _status = "Pilih PSD master untuk memulai.";
    private string _templateStatus = "Belum ada PSD master";
    private string _templatePreviewNotice = "Pilih PSD master untuk menampilkan composite tersimpan.";
    private SourcePhoto? _selectedPhoto;
    private FrameSlot? _selectedSlot;
    private byte[]? _templatePreviewPng;
    private int? _documentWidth;
    private int? _documentHeight;
    private double _zoom = 1;
    private double _rotation;
    private double _offsetX;
    private double _offsetY;
    private bool _isFitMode;
    private bool _isInspecting;
    private bool _isExporting;
    private int _inspectionGeneration;

    public MainViewModel(IPsdTemplateAdapter? psdAdapter = null)
    {
        _psdAdapter = psdAdapter ?? new PsdTemplateEngineUiAdapter();
        Slots = new ObservableCollection<FrameSlot>();
    }

    public ObservableCollection<FrameSlot> Slots { get; }
    public ObservableCollection<SourcePhoto> SourcePhotos { get; } = new();
    public bool PsdEngineReady => _psdAdapter.IsAvailable;
    public string EngineNotice => PsdEngineReady
        ? "Composite PSD dibaca apa adanya; jenis slot hanya perkiraan dari nama layer."
        : "Mesin PSD tidak tersedia.";
    public string ExportNotice => "Ekspor hanya membuat PNG composite tersimpan. Foto, teks, logo, dan crop tidak ditulis ke PSD.";
    public string MasterPath { get => _masterPath; set { _masterPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMaster)); OnPropertyChanged(nameof(CanExportFlattenedPreview)); } }
    public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }
    public string OutputPath { get => _outputPath; set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportFlattenedPreview)); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string TemplateStatus { get => _templateStatus; set { _templateStatus = value; OnPropertyChanged(); } }
    public string TemplatePreviewNotice { get => _templatePreviewNotice; private set { _templatePreviewNotice = value; OnPropertyChanged(); } }
    public bool HasMaster => !string.IsNullOrWhiteSpace(MasterPath);
    public bool HasTemplatePreview => _templatePreviewPng is { Length: > 0 };
    public bool HasSelectedPhoto => SelectedPhoto is not null;
    public bool HasPreviewImage => SelectedPhoto?.Thumbnail is not null;
    public bool HasPreviewUnavailable => SelectedPhoto is not null && SelectedPhoto.Thumbnail is null;
    public bool HasSelectedLogo => SelectedLogo is not null;
    public bool HasTextCandidateSelected => SelectedSlot?.IsText == true;
    public bool IsInspecting { get => _isInspecting; private set { _isInspecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportFlattenedPreview)); } }
    public bool IsExporting { get => _isExporting; private set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportFlattenedPreview)); } }
    public string DocumentDimensions => _documentWidth is { } width && _documentHeight is { } height
        ? $"{width:N0} × {height:N0} px"
        : "";
    public byte[]? TemplatePreviewPng => _templatePreviewPng;
    public SourcePhoto? SelectedPhoto { get => _selectedPhoto; set { _selectedPhoto = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedPhoto)); OnPropertyChanged(nameof(HasPreviewImage)); OnPropertyChanged(nameof(HasPreviewUnavailable)); } }
    public SourcePhoto? SelectedLogo => Slots.FirstOrDefault(slot => slot.IsLogo)?.Photo;
    public FrameSlot? SelectedSlot { get => _selectedSlot; private set { _selectedSlot = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTextCandidateSelected)); } }
    public double Zoom { get => _zoom; set { _zoom = Math.Clamp(value, 0.5, 3); OnPropertyChanged(); } }
    public double Rotation { get => _rotation; set { _rotation = value % 360; OnPropertyChanged(); } }
    public double OffsetX { get => _offsetX; set { _offsetX = value; OnPropertyChanged(); } }
    public double OffsetY { get => _offsetY; set { _offsetY = value; OnPropertyChanged(); } }
    public bool IsFitMode { get => _isFitMode; private set { _isFitMode = value; OnPropertyChanged(); } }
    public bool CanExportFlattenedPreview => PsdEngineReady && HasMaster && HasTemplatePreview && !IsInspecting && !IsExporting && !string.IsNullOrWhiteSpace(OutputPath);

    public void SelectSlot(FrameSlot? slot)
    {
        foreach (var item in Slots)
            item.IsSelected = ReferenceEquals(item, slot);
        SelectedSlot = slot;
        if (slot?.IsPhoto == true)
        {
            if (slot.Photo is not null)
                SelectedPhoto = slot.Photo;
            ResetCrop();
        }
    }

    public void AddSourcePhoto(SourcePhoto photo)
    {
        if (SourcePhotos.Any(existing => string.Equals(existing.Path, photo.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        SourcePhotos.Add(photo);
        SelectedPhoto = photo;
        Status = $"{photo.FileName} siap untuk pratinjau lokal.";
    }

    public bool AssignSelectedPhoto(FrameSlot? slot)
    {
        if (slot is null || (!slot.IsPhoto && !slot.IsLogo) || SelectedPhoto is null)
            return false;

        slot.Photo = SelectedPhoto;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        SelectSlot(slot);
        Status = $"{SelectedPhoto.FileName} ditampilkan sebagai pratinjau lokal pada {slot.Label}; PSD tidak diubah.";
        return true;
    }

    public void SetMaster(string path)
    {
        _inspectionGeneration++;
        IsInspecting = false;
        MasterPath = path;
        ClearInspectionState();
        SelectedPhoto = null;
        TemplateStatus = "PSD dipilih · belum dibaca";
        TemplatePreviewNotice = "Membaca composite tersimpan dari PSD…";
        Status = "File master dipilih. Memeriksa composite dan nama layer.";
    }

    public async Task InspectSelectedMasterAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MasterPath))
            return;

        var masterPath = MasterPath;
        var generation = ++_inspectionGeneration;
        ClearInspectionState();
        IsInspecting = true;
        TemplateStatus = "Membaca PSD…";
        TemplatePreviewNotice = "Membaca composite tersimpan dari PSD…";
        Status = "Memeriksa file PSD dan kandidat nama layer…";

        try
        {
            var result = await _psdAdapter.InspectAsync(masterPath, cancellationToken);
            if (generation != _inspectionGeneration)
                return;

            if (!result.Success)
            {
                TemplateStatus = "Pemeriksaan gagal";
                TemplatePreviewNotice = "Composite tidak tersedia. Periksa pesan status dan pilih file PSD yang didukung.";
                Status = result.Message;
                return;
            }

            foreach (var descriptor in result.Slots)
            {
                var kind = descriptor.Type switch
                {
                    PsdSlotType.Photo => SlotKind.Photo,
                    PsdSlotType.Text => SlotKind.Text,
                    PsdSlotType.Logo => SlotKind.Logo,
                    _ => SlotKind.Photo
                };
                var layerName = string.IsNullOrWhiteSpace(descriptor.LayerName) ? "(nama kosong)" : descriptor.LayerName;
                Slots.Add(new FrameSlot(descriptor.Id, descriptor.DisplayName, kind, $"Ditebak dari nama layer · {layerName}", descriptor.CurrentValue));
            }

            _documentWidth = result.DocumentWidth;
            _documentHeight = result.DocumentHeight;
            _templatePreviewPng = result.FlattenedPreviewPng;
            OnPropertyChanged(nameof(TemplatePreviewPng));
            OnPropertyChanged(nameof(HasTemplatePreview));
            OnPropertyChanged(nameof(DocumentDimensions));
            OnPropertyChanged(nameof(CanExportFlattenedPreview));

            TemplateStatus = _documentWidth is { } width && _documentHeight is { } height
                ? $"{Slots.Count} kandidat nama layer · {width:N0} × {height:N0} px"
                : $"{Slots.Count} kandidat nama layer";
            TemplatePreviewNotice = HasTemplatePreview
                ? "Composite tersimpan dari PSD. Pratinjau hanya-baca; layer tidak dirender ulang."
                : "Metadata dibaca, tetapi tidak ada composite RGB yang didukung untuk pratinjau.";
            SelectSlot(Slots.FirstOrDefault());
            Status = result.Message;
            OnPropertyChanged(nameof(SelectedLogo));
            OnPropertyChanged(nameof(HasSelectedLogo));
        }
        catch (OperationCanceledException)
        {
            if (generation == _inspectionGeneration)
            {
                TemplateStatus = "Pemeriksaan dibatalkan";
                TemplatePreviewNotice = "Pilih atau buka kembali PSD untuk memulai pemeriksaan.";
                Status = "Pemeriksaan PSD dibatalkan.";
            }
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (generation == _inspectionGeneration)
            {
                ClearInspectionState();
                TemplateStatus = "Pemeriksaan gagal";
                TemplatePreviewNotice = "PSD tidak dapat diperiksa. Periksa file dan coba lagi.";
                Status = $"PSD inspection failed: {exception.Message}";
            }
        }
        finally
        {
            if (generation == _inspectionGeneration)
                IsInspecting = false;
        }
    }

    public async Task ExportFlattenedPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExportFlattenedPreview)
            return;

        IsExporting = true;
        Status = "Membuat PNG composite tersimpan. Isi slot dan perubahan crop tidak diterapkan.";
        try
        {
            var request = new PsdExportRequest(
                MasterPath,
                SourcePath,
                OutputPath,
                new Dictionary<string, string>());
            var result = await _psdAdapter.ExportCopyAsync(request, cancellationToken);
            Status = result.Success && !string.IsNullOrWhiteSpace(result.OutputPath)
                ? $"{result.Message} · {result.OutputPath}"
                : result.Message;
        }
        catch (OperationCanceledException)
        {
            Status = "Ekspor pratinjau PNG dibatalkan.";
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Status = $"Ekspor pratinjau PNG gagal: {exception.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    public void ResetCrop()
    {
        IsFitMode = false;
        Zoom = 1;
        Rotation = 0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetPreviewMode(bool showWholePhoto)
    {
        IsFitMode = showWholePhoto;
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void ClearQueue()
    {
        SourcePhotos.Clear();
        SelectedPhoto = null;
        foreach (var slot in Slots.Where(slot => slot.IsPhoto || slot.IsLogo))
            slot.Photo = null;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        Status = "Antrean foto dikosongkan. PSD dan composite tetap tidak berubah.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ClearInspectionState()
    {
        SelectSlot(null);
        Slots.Clear();
        _templatePreviewPng = null;
        _documentWidth = null;
        _documentHeight = null;
        OnPropertyChanged(nameof(TemplatePreviewPng));
        OnPropertyChanged(nameof(HasTemplatePreview));
        OnPropertyChanged(nameof(DocumentDimensions));
        OnPropertyChanged(nameof(CanExportFlattenedPreview));
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
