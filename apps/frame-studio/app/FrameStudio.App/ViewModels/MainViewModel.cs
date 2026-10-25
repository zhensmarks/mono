using FrameStudio.App.Models;
using FrameStudio.App.Services;
using FrameStudio.Engine;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
    private FramePsdLayerTreeSummary? _layerTreeSummary;
    private FramePsdWorkflowHints? _workflowHints;
    private double _zoom = 1;
    private double _rotation;
    private double _offsetX;
    private double _offsetY;
    private bool _isFitMode;
    private bool _isInspecting;
    private bool _isExporting;
    private bool _isFallbackPreviewMode;
    private bool _isManualFrameDrawing;
    private int _inspectionGeneration;
    private const int MaximumManualFrameMaskPixels = 4 * 1024 * 1024;

    public MainViewModel(IPsdTemplateAdapter? psdAdapter = null)
    {
        _psdAdapter = psdAdapter ?? new PsdTemplateEngineUiAdapter();
        Slots = new ObservableCollection<FrameSlot>();
        LayerTreeRows = new ObservableCollection<PsdLayerTreeRow>();
    }

    public ObservableCollection<FrameSlot> Slots { get; }
    public ObservableCollection<PsdLayerTreeRow> LayerTreeRows { get; }
    public ObservableCollection<SourcePhoto> SourcePhotos { get; } = new();
    public bool PsdEngineReady => _psdAdapter.IsAvailable;
    public string RuntimePlatformLabel => $"{OperatingSystemLabel()} {ArchitectureLabel()}";
    public string EngineNotice => _psdAdapter is PsdTemplateEngineUiAdapter uiAdapter
        ? uiAdapter.CapabilityNotice
        : PsdEngineReady
            ? $"{RuntimePlatformLabel} · PsdSharp 1.2.0 · hanya-baca · PSD berlapis nonaktif"
            : $"{RuntimePlatformLabel} · pembaca PSD tidak tersedia · PSD berlapis nonaktif";
    public string EngineDetails => _psdAdapter is PsdTemplateEngineUiAdapter uiAdapter
        ? uiAdapter.CapabilityDetails
        : $"Runtime platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). Layered PSD writing is disabled; UI placement and PNG export require a validated photo slot.";
    public string ExportNotice => IsFallbackPreviewMode && !HasManualPhotoFrame
        ? "Ekspor slot PSD nonaktif; gambar frame foto persegi manual untuk PNG copy."
        : !HasValidPhotoSlotSetup
            ? "Ekspor PNG nonaktif; gunakan aperture terverifikasi atau gambar frame foto persegi manual."
            : "Ekspor membuat PNG copy terpisah. Frame manual hanya overlay; PSD master/layer tidak berubah.";
    public string MasterPath { get => _masterPath; set { _masterPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMaster)); OnPropertyChanged(nameof(MasterPathDisplayText)); OnPropertyChanged(nameof(HasValidPhotoSlotSetup)); OnPropertyChanged(nameof(HasManualPhotoFrame)); OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice)); OnPropertyChanged(nameof(EmptyInspectorText)); OnPropertyChanged(nameof(CanPlaceSelectedSlot)); OnPropertyChanged(nameof(CanAdjustSelectedSlot)); OnPropertyChanged(nameof(IsImageSlotSelected)); OnPropertyChanged(nameof(CanExportPngCopies)); OnPropertyChanged(nameof(CanUseManualFrameTool)); OnPropertyChanged(nameof(CanManageSourcePhotos)); } }
    public string MasterPathDisplayText => HasMaster ? Path.GetFileName(MasterPath) : string.Empty;
    public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }
    public string OutputPath { get => _outputPath; set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportPngCopies)); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string TemplateStatus { get => _templateStatus; set { _templateStatus = value; OnPropertyChanged(); } }
    public string TemplatePreviewNotice { get => _templatePreviewNotice; private set { _templatePreviewNotice = value; OnPropertyChanged(); } }
    public bool HasMaster => !string.IsNullOrWhiteSpace(MasterPath);
    public string EmptyInspectorText => !HasMaster
        ? "Pilih PSD master untuk memuat pratinjau."
        : HasValidPhotoSlotSetup
            ? "Pilih area untuk melihat detailnya."
            : "Slot foto otomatis belum siap. Gambar rectangle manual untuk menaruh foto pada PNG copy.";
    public bool HasValidPhotoSlotSetup => Slots.Any(slot => slot.IsPhoto && slot.HasTrustedApertureMask);
    public bool HasManualPhotoFrame => Slots.Any(slot => slot.IsManualFrame && slot.IsPhoto && slot.HasTrustedApertureMask);
    public bool ShowPhotoSlotSetupNotice => HasMaster && !HasValidPhotoSlotSetup;
    public string PhotoSlotSetupNotice => "Slot foto PSD otomatis belum memiliki aperture biner yang terverifikasi. Gambar rectangle manual untuk PNG copy; frame ini tidak menjadi layer PSD.";
    public string WorkflowStructureNotice => _workflowHints?.ToSafeSummary()
        ?? "Struktur grup Single/KOLASE belum tersedia; nama grup saja tidak membuka placement.";
    public bool HasTemplatePreview => _templatePreviewPng is { Length: > 0 };
    public bool CanManageSourcePhotos => HasTemplatePreview && !IsInspecting && !IsExporting;
    public bool CanUseManualFrameTool => HasTemplatePreview && DocumentWidth > 0 && DocumentHeight > 0 && !IsInspecting && !IsExporting;
    public bool IsManualFrameDrawing
    {
        get => _isManualFrameDrawing;
        private set
        {
            if (_isManualFrameDrawing == value) return;
            _isManualFrameDrawing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualFrameToolLabel));
            OnPropertyChanged(nameof(ManualFrameToolHelp));
        }
    }
    public string ManualFrameToolLabel => IsManualFrameDrawing ? "Batal frame" : "Frame foto";
    public string ManualFrameToolHelp => IsManualFrameDrawing
        ? "Seret rectangle pada kanvas. Frame manual memakai mask biner penuh; tepi lembut dan bentuk bebas belum didukung."
        : "Gambar rectangle di atas komposit. Hasilnya overlay PNG, bukan perubahan layer PSD.";
    public bool HasSelectedPhoto => SelectedPhoto is not null;
    public bool HasPreviewImage => SelectedPhoto?.Image is not null;
    public bool HasPreviewUnavailable => SelectedPhoto is not null && SelectedPhoto.Image is null;
    public bool HasSelectedLogo => SelectedLogo is not null;
    public bool HasTextCandidateSelected => SelectedSlot?.IsText == true;
    public bool HasSelectedSlot => SelectedSlot is not null;
    public bool ShowEmptyInspector => !IsFallbackPreviewMode && !HasSelectedSlot && !ShowPhotoSlotSetupNotice;
    public bool ShowDetectedAreaList => Slots.Count > 0 && (!IsFallbackPreviewMode || Slots.Any(slot => slot.IsManualFrame));
    public bool IsFallbackPreviewMode
    {
        get => _isFallbackPreviewMode;
        private set
        {
            if (_isFallbackPreviewMode == value) return;
            _isFallbackPreviewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditPhotoTextLogo));
            OnPropertyChanged(nameof(CanManageSourcePhotos));
            OnPropertyChanged(nameof(CanUseManualFrameTool));
            OnPropertyChanged(nameof(HasManualPhotoFrame));
            OnPropertyChanged(nameof(CanPlaceSelectedSlot));
            OnPropertyChanged(nameof(CanAdjustSelectedSlot));
            OnPropertyChanged(nameof(IsImageSlotSelected));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
            OnPropertyChanged(nameof(ExportNotice));
            OnPropertyChanged(nameof(CanExportPngCopies));
            OnPropertyChanged(nameof(EngineNotice));
            OnPropertyChanged(nameof(EngineDetails));
            OnPropertyChanged(nameof(LayerTreeSummaryText));
            OnPropertyChanged(nameof(ShowEmptyInspector));
            OnPropertyChanged(nameof(ShowDetectedAreaList));
            OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice));
            OnPropertyChanged(nameof(HasManualPhotoFrame));
        }
    }
    public bool CanEditPhotoTextLogo => !IsFallbackPreviewMode && !IsInspecting && !IsExporting;
    public bool CanAdjustSelectedSlot => CanPlaceSlot(SelectedSlot);
    public bool CanPlaceSelectedSlot => CanPlaceSlot(SelectedSlot);
    public bool CanPlaceSlot(FrameSlot? slot) => HasMaster
        && !IsInspecting
        && !IsExporting
        && slot is { HasTrustedApertureMask: true } target
        && (target.IsPhoto || target.IsLogo)
        && (target.IsManualFrame || (!IsFallbackPreviewMode && HasValidPhotoSlotSetup));
    public bool CanAssignSelectedPhoto => CanPlaceSelectedSlot && SelectedPhoto is not null;
    public string LayerTreeSummaryText => _layerTreeSummary is { } summary
        ? $"{summary.LayerCount} layer · {summary.GroupCount} grup · {summary.TextLayerCount} teks · {summary.MaskCount} mask · {summary.VectorMaskCount} mask vektor · {summary.EffectLayerCount} efek"
        : "Rincian layer belum tersedia.";
    public string ActiveReaderLabel { get; private set; } = "";
    public int DocumentWidth => _documentWidth ?? 0;
    public int DocumentHeight => _documentHeight ?? 0;
    public bool IsInspecting
    {
        get => _isInspecting;
        private set
        {
            _isInspecting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExportPngCopies));
            OnPropertyChanged(nameof(CanEditPhotoTextLogo));
            OnPropertyChanged(nameof(CanManageSourcePhotos));
            OnPropertyChanged(nameof(CanUseManualFrameTool));
            OnPropertyChanged(nameof(CanPlaceSelectedSlot));
            OnPropertyChanged(nameof(CanAdjustSelectedSlot));
            OnPropertyChanged(nameof(IsImageSlotSelected));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
            OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice));
        }
    }
    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            _isExporting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExportPngCopies));
            OnPropertyChanged(nameof(CanEditPhotoTextLogo));
            OnPropertyChanged(nameof(CanManageSourcePhotos));
            OnPropertyChanged(nameof(CanUseManualFrameTool));
            OnPropertyChanged(nameof(CanPlaceSelectedSlot));
            OnPropertyChanged(nameof(CanAdjustSelectedSlot));
            OnPropertyChanged(nameof(IsImageSlotSelected));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
        }
    }
    public string DocumentDimensions => _documentWidth is { } width && _documentHeight is { } height
        ? $"{width:N0} × {height:N0} px"
        : "";
    public byte[]? TemplatePreviewPng => _templatePreviewPng;
    public SourcePhoto? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            _selectedPhoto = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPhoto));
            OnPropertyChanged(nameof(HasPreviewImage));
            OnPropertyChanged(nameof(HasPreviewUnavailable));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
        }
    }
    public SourcePhoto? SelectedLogo => Slots.FirstOrDefault(slot => slot.IsLogo)?.Photo;
    public FrameSlot? SelectedSlot
    {
        get => _selectedSlot;
        private set
        {
            _selectedSlot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTextCandidateSelected));
            OnPropertyChanged(nameof(HasSelectedSlot));
            OnPropertyChanged(nameof(ShowEmptyInspector));
            OnPropertyChanged(nameof(IsImageSlotSelected));
            OnPropertyChanged(nameof(CanPlaceSelectedSlot));
            OnPropertyChanged(nameof(CanAdjustSelectedSlot));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
            OnPropertyChanged(nameof(Zoom));
            OnPropertyChanged(nameof(ZoomLabel));
            OnPropertyChanged(nameof(Rotation));
            OnPropertyChanged(nameof(RotationLabel));
            OnPropertyChanged(nameof(OffsetX));
            OnPropertyChanged(nameof(OffsetY));
            OnPropertyChanged(nameof(IsFitMode));
        }
    }
    public double Zoom { get => SelectedSlot?.Zoom ?? _zoom; set { if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true) return; var zoom = Math.Clamp(value, 0.5, 3); if (SelectedSlot is { } slot) slot.Zoom = zoom; else _zoom = zoom; OnPropertyChanged(); OnPropertyChanged(nameof(ZoomLabel)); } }
    public double Rotation { get => SelectedSlot?.Rotation ?? _rotation; set { if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true) return; var rotation = value % 360; if (SelectedSlot is { } slot) slot.Rotation = rotation; else _rotation = rotation; OnPropertyChanged(); OnPropertyChanged(nameof(RotationLabel)); } }
    public double OffsetX { get => SelectedSlot?.OffsetX ?? _offsetX; set { if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true) return; if (SelectedSlot is { } slot) slot.OffsetX = value; else _offsetX = value; OnPropertyChanged(); } }
    public double OffsetY { get => SelectedSlot?.OffsetY ?? _offsetY; set { if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true) return; if (SelectedSlot is { } slot) slot.OffsetY = value; else _offsetY = value; OnPropertyChanged(); } }
    public bool IsFitMode { get => SelectedSlot?.IsFitMode ?? _isFitMode; private set { if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true) return; if (SelectedSlot is { } slot) slot.IsFitMode = value; else _isFitMode = value; OnPropertyChanged(); } }
    public bool IsImageSlotSelected => CanPlaceSelectedSlot;
    public string ZoomLabel => $"{Zoom:P0}";
    public string RotationLabel => $"{Rotation:0.#}°";
    public bool CanExportPngCopies => PsdEngineReady
        && HasMaster
        && HasTemplatePreview
        && HasValidPhotoSlotSetup
        && (!IsFallbackPreviewMode || HasManualPhotoFrame)
        && !IsInspecting
        && !IsExporting
        && !string.IsNullOrWhiteSpace(OutputPath);

    private static string OperatingSystemLabel() => OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Platform lain";

    private static string ArchitectureLabel() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "ARM64",
        Architecture.X86 => "x86",
        Architecture.Arm => "ARM",
        _ => RuntimeInformation.ProcessArchitecture.ToString()
    };

    public void SetExporting(bool exporting) => IsExporting = exporting;

    public void ToggleManualFrameTool()
    {
        if (!CanUseManualFrameTool)
            return;
        IsManualFrameDrawing = !IsManualFrameDrawing;
        Status = IsManualFrameDrawing
            ? "Frame Tool aktif · seret rectangle pada pratinjau untuk membuat overlay PNG."
            : "Frame Tool dibatalkan.";
    }

    public FrameSlot? AddManualPhotoFrame(int x, int y, int width, int height)
    {
        if (!CanUseManualFrameTool || width <= 1 || height <= 1 || x < 0 || y < 0
            || (long)x + width > DocumentWidth || (long)y + height > DocumentHeight)
        {
            Status = "Frame manual ditolak: buat rectangle di dalam batas pratinjau PSD.";
            return null;
        }

        var pixelCount = (long)width * height;
        if (pixelCount > MaximumManualFrameMaskPixels)
        {
            Status = $"Frame manual ditolak: mask persegi dibatasi {MaximumManualFrameMaskPixels:N0} piksel.";
            return null;
        }

        var mask = Enumerable.Repeat((byte)255, (int)pixelCount).ToArray();
        var frameIndex = Slots.Count(slot => slot.IsManualFrame) + 1;
        var frame = new FrameSlot(
            $"manual-frame-{frameIndex}",
            $"Frame manual {frameIndex}",
            SlotKind.Photo,
            "Rectangle biner buatan pengguna; hanya PNG copy, tidak disimpan ke PSD.",
            bounds: new FrameSlotBounds(x, y, width, height),
            isPlacementReady: true,
            apertureMask: mask,
            isManualFrame: true);

        if (SelectedPhoto is not null)
            frame.Photo = SelectedPhoto;
        Slots.Add(frame);
        SelectSlot(frame);
        IsManualFrameDrawing = false;
        OnPropertyChanged(nameof(HasValidPhotoSlotSetup));
        OnPropertyChanged(nameof(HasManualPhotoFrame));
        OnPropertyChanged(nameof(ShowDetectedAreaList));
        OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice));
        OnPropertyChanged(nameof(EmptyInspectorText));
        OnPropertyChanged(nameof(CanExportPngCopies));
        OnPropertyChanged(nameof(ExportNotice));
        OnPropertyChanged(nameof(CanPlaceSelectedSlot));
        OnPropertyChanged(nameof(CanAdjustSelectedSlot));
        OnPropertyChanged(nameof(CanAssignSelectedPhoto));
        var createdBounds = frame.Bounds!.Value;
        Status = $"Rectangle {createdBounds.Width} × {createdBounds.Height} px dibuat sebagai overlay PNG terpisah.";
        return frame;
    }

    public void SelectSlot(FrameSlot? slot)
    {
        if (IsFallbackPreviewMode && slot is not null && !slot.IsManualFrame)
            return;

        foreach (var item in Slots)
            item.IsSelected = ReferenceEquals(item, slot);
        SelectedSlot = slot;
        if (slot?.Photo is not null)
            SelectedPhoto = slot.Photo;
    }

    public void AddSourcePhoto(SourcePhoto photo)
    {
        if (!CanManageSourcePhotos)
        {
            photo.Dispose();
            return;
        }

        if (SourcePhotos.Any(existing => string.Equals(existing.Path, photo.Path, StringComparison.OrdinalIgnoreCase)))
        {
            photo.Dispose();
            return;
        }

        SourcePhotos.Add(photo);
        SelectedPhoto = photo;
        Status = $"{photo.FileName} siap untuk pratinjau lokal.";
    }

    public bool AssignSelectedPhoto(FrameSlot? slot)
    {
        if (slot is null || !CanPlaceSlot(slot) || SelectedPhoto is not { } selectedPhoto)
            return false;

        slot.Photo = selectedPhoto;
        slot.Zoom = 1;
        slot.Rotation = 0;
        slot.OffsetX = 0;
        slot.OffsetY = 0;
        slot.IsFitMode = false;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        SelectSlot(slot);
        Status = $"{selectedPhoto.FileName} ditempatkan pada {slot.Label}; placement ikut PNG ekspor, PSD master tidak diubah.";
        return true;
    }

    public void SetMaster(string path)
    {
        _inspectionGeneration++;
        IsInspecting = false;
        IsFallbackPreviewMode = false;
        ActiveReaderLabel = string.Empty;
        OnPropertyChanged(nameof(ActiveReaderLabel));
        MasterPath = path;
        ClearInspectionState();
        SelectedPhoto = null;
        TemplateStatus = "PSD dipilih · belum dibaca";
        TemplatePreviewNotice = "Memuat pratinjau PSD…";
        Status = "PSD lokal dipilih. Memeriksa pratinjau dan struktur dokumen secara hanya-baca.";
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
        TemplatePreviewNotice = "Memuat pratinjau PSD…";
        Status = "Memeriksa PSD lokal dengan pembaca hanya-baca…";

        try
        {
            var result = await _psdAdapter.InspectAsync(masterPath, cancellationToken);
            if (generation != _inspectionGeneration)
                return;

            if (!result.Success)
            {
                TemplateStatus = "Pemeriksaan gagal";
                TemplatePreviewNotice = "Pratinjau belum tersedia. Periksa status pembacaan di bawah.";
                Status = SafeInspectionFailure(result.FailureKind);
                OnPropertyChanged(nameof(EngineNotice));
                OnPropertyChanged(nameof(EngineDetails));
                return;
            }

            var usedFallback = string.Equals(result.ActiveReaderName, "psd-tools desktop sidecar", StringComparison.Ordinal);
            var usedApertureSupplement = string.Equals(result.ActiveReaderName, "PsdSharp + psd-tools aperture supplement", StringComparison.Ordinal);
            var hasExactImageMap = result.Slots.Any(slot =>
                (slot.Type is PsdSlotType.Photo or PsdSlotType.Logo)
                && slot.IsPlacementReady
                && slot.Bounds is { Width: > 0, Height: > 0 } slotBounds
                && slot.ApertureMask is { Length: > 0 } exactMask
                && exactMask.LongLength == (long)slotBounds.Width * slotBounds.Height
                && exactMask.Any(sample => sample == 255)
                && exactMask.All(sample => sample is 0 or 255));
            IsFallbackPreviewMode = usedFallback && !hasExactImageMap;
            ActiveReaderLabel = usedFallback
                ? "psd-tools 1.26.0 (local sidecar)"
                : usedApertureSupplement
                    ? "PsdSharp 1.2.0 + psd-tools aperture map"
                    : "PsdSharp 1.2.0";
            OnPropertyChanged(nameof(ActiveReaderLabel));
            _layerTreeSummary = result.LayerTree;
            _workflowHints = result.WorkflowHints;
            OnPropertyChanged(nameof(WorkflowStructureNotice));
            LayerTreeRows.Clear();
            if (IsFallbackPreviewMode && result.LayerTree is { } tree)
                AddLayerRows(tree.Roots);

            if (!IsFallbackPreviewMode)
            {
                var typeCounts = new Dictionary<SlotKind, int>();
                var slotIndex = 0;
                foreach (var descriptor in result.Slots)
                {
                    var kind = descriptor.Type switch
                    {
                        PsdSlotType.Photo => SlotKind.Photo,
                        PsdSlotType.Text => SlotKind.Text,
                        PsdSlotType.Logo => SlotKind.Logo,
                        _ => SlotKind.Photo
                    };
                    typeCounts.TryGetValue(kind, out var typeCount);
                    typeCount++;
                    typeCounts[kind] = typeCount;
                    var kindLabel = kind switch
                    {
                        SlotKind.Photo => "Foto",
                        SlotKind.Text => "Teks",
                        SlotKind.Logo => "Logo",
                        _ => "Slot"
                    };
                    var safeLabel = $"{kindLabel} {typeCount}";
                    var safeId = $"slot/{++slotIndex}";
                    FrameSlotBounds? bounds = descriptor.Bounds is { } rectangle
                        ? new FrameSlotBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)
                        : null;
                    var isImageTarget = kind is SlotKind.Photo or SlotKind.Logo;
                    var placementReady = descriptor.IsPlacementReady && HasValidApertureMap(bounds, descriptor.ApertureMask, result.DocumentWidth, result.DocumentHeight);
                    if (isImageTarget && !placementReady)
                        continue;
                    var boundsHint = bounds is { } estimated
                        ? $" · area perkiraan {estimated.Width:N0} × {estimated.Height:N0} px"
                        : " · ukuran belum diketahui";
                    Slots.Add(new FrameSlot(safeId, safeLabel, kind, $"Posisi otomatis{boundsHint}", null, bounds, placementReady, placementReady ? descriptor.ApertureMask : null));
                }
            }
            else if (IsFallbackPreviewMode)
            {
                ClearSourcePhotosForReadOnlyPreview();
                SourcePath = string.Empty;
            }

            _documentWidth = result.DocumentWidth;
            _documentHeight = result.DocumentHeight;
            _templatePreviewPng = result.FlattenedPreviewPng;
            OnPropertyChanged(nameof(TemplatePreviewPng));
            OnPropertyChanged(nameof(HasTemplatePreview));
            OnPropertyChanged(nameof(DocumentDimensions));
            OnPropertyChanged(nameof(DocumentWidth));
            OnPropertyChanged(nameof(DocumentHeight));
            OnPropertyChanged(nameof(HasValidPhotoSlotSetup));
            OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice));
            OnPropertyChanged(nameof(EmptyInspectorText));
            OnPropertyChanged(nameof(CanPlaceSelectedSlot));
            OnPropertyChanged(nameof(IsImageSlotSelected));
            OnPropertyChanged(nameof(CanAssignSelectedPhoto));
            OnPropertyChanged(nameof(CanExportPngCopies));
            OnPropertyChanged(nameof(ShowDetectedAreaList));

            var validPhotoSlots = Slots.Count(slot => slot.IsPhoto && slot.HasTrustedApertureMask);
            TemplateStatus = IsFallbackPreviewMode
                ? _documentWidth is { } previewWidth && _documentHeight is { } previewHeight
                    ? $"Pratinjau siap · {previewWidth:N0} × {previewHeight:N0} px"
                    : "Pratinjau siap"
                : validPhotoSlots == 0
                    ? _documentWidth is { } width && _documentHeight is { } height
                        ? $"Slot foto belum disiapkan · {width:N0} × {height:N0} px"
                        : "Slot foto belum disiapkan"
                    : _documentWidth is { } imageWidth && _documentHeight is { } imageHeight
                        ? $"{validPhotoSlots} slot foto siap · {imageWidth:N0} × {imageHeight:N0} px"
                        : $"{validPhotoSlots} slot foto siap";
            TemplatePreviewNotice = HasTemplatePreview
                ? IsFallbackPreviewMode
                    ? "Pratinjau komposit tersimpan dari PSD lokal. Layer hanya-baca; frame rectangle manual dapat diekspor sebagai PNG copy."
                    : usedApertureSupplement
                        ? "Komposit tersimpan dari pembaca utama; aperture biner yang tervalidasi dipakai sama untuk pratinjau dan PNG copy."
                        : "Komposit tersimpan dari PSD. Pratinjau hanya-baca; layer tidak disusun ulang."
                : "Informasi PSD terbaca, tetapi gambarnya belum dapat ditampilkan.";
            SelectSlot(IsFallbackPreviewMode ? null : Slots.FirstOrDefault());
            Status = IsFallbackPreviewMode
                ? "Pratinjau tersimpan siap. Penempatan otomatis terkunci; gunakan Frame Tool untuk overlay PNG persegi manual."
                : usedApertureSupplement && hasExactImageMap
                    ? "Aperture biner cocok dengan komposit tersimpan; placement dirender dengan mask yang sama untuk pratinjau dan PNG. PSD master tetap hanya-baca."
                    : HasValidPhotoSlotSetup
                    ? "Pemeriksaan PSD selesai. Area foto valid terdeteksi; PSD master tetap tidak berubah."
                    : "Pemeriksaan PSD selesai. Slot foto belum disiapkan; penempatan dan ekspor nonaktif.";
            OnPropertyChanged(nameof(SelectedLogo));
            OnPropertyChanged(nameof(HasSelectedLogo));
            OnPropertyChanged(nameof(LayerTreeSummaryText));
            OnPropertyChanged(nameof(EngineNotice));
            OnPropertyChanged(nameof(EngineDetails));
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
                TemplatePreviewNotice = "Pratinjau belum tersedia. Periksa dukungan pembaca.";
                Status = "Pemeriksaan PSD gagal. File sumber tidak diubah; periksa dukungan pembaca.";
                OnPropertyChanged(nameof(EngineNotice));
                OnPropertyChanged(nameof(EngineDetails));
            }
        }
        finally
        {
            if (generation == _inspectionGeneration)
                IsInspecting = false;
        }
    }

    public void ResetCrop()
    {
        if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true)
            return;

        IsFitMode = false;
        Zoom = 1;
        Rotation = 0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetPreviewMode(bool showWholePhoto)
    {
        if (IsFallbackPreviewMode && SelectedSlot?.IsManualFrame != true)
            return;

        IsFitMode = showWholePhoto;
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void ClearQueue()
    {
        var oldPhotos = SourcePhotos.ToArray();
        SourcePhotos.Clear();
        SelectedPhoto = null;
        foreach (var slot in Slots.Where(slot => slot.IsPhoto || slot.IsLogo))
            slot.Photo = null;
        foreach (var photo in oldPhotos)
            photo.Dispose();
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        Status = "Antrean foto dikosongkan. PSD dan composite tetap tidak berubah.";
    }

    private void ClearSourcePhotosForReadOnlyPreview()
    {
        var oldPhotos = SourcePhotos.ToArray();
        SourcePhotos.Clear();
        SelectedPhoto = null;
        foreach (var photo in oldPhotos)
            photo.Dispose();
    }

    private void AddLayerRows(
        IReadOnlyList<FramePsdLayerSummary> nodes,
        string parentPath = "",
        int depth = 0,
        Dictionary<string, int>? kindCounts = null)
    {
        kindCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var path = string.IsNullOrEmpty(parentPath) ? (index + 1).ToString() : $"{parentPath}.{index + 1}";
            var kind = SafeLayerKind(node.Kind, node.IsGroup);
            kindCounts.TryGetValue(kind, out var count);
            count++;
            kindCounts[kind] = count;

            var detailParts = new List<string>
            {
                $"{Math.Max(0, node.Bounds.Width):N0} × {Math.Max(0, node.Bounds.Height):N0} px",
                node.IsVisible ? "visible" : "hidden",
                $"opacity {node.Opacity / 255d:P0}"
            };
            if (node.HasMask) detailParts.Add("mask");
            if (node.HasVectorMask) detailParts.Add("vector mask");
            if (node.HasClipping) detailParts.Add("clipping");
            if (node.HasEffects) detailParts.Add("effects");
            if (node.HasPixelData) detailParts.Add("pixel data");

            LayerTreeRows.Add(new PsdLayerTreeRow(
                path,
                $"{kind} {count}",
                kind,
                string.Join(" · ", detailParts),
                depth == 0 ? string.Empty : $"{new string(' ', Math.Min(depth, 12) * 2)}↳ "));
            AddLayerRows(node.Children, path, depth + 1, kindCounts);
        }
    }

    private static string SafeLayerKind(string? kind, bool isGroup)
    {
        if (isGroup || string.Equals(kind, "Group", StringComparison.OrdinalIgnoreCase)) return "Group";
        if (string.Equals(kind, "Text layer", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase)) return "Text layer";
        if (string.Equals(kind, "Pixel layer", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "Pixel", StringComparison.OrdinalIgnoreCase)) return "Pixel layer";
        if (string.Equals(kind, "Shape layer", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "Shape", StringComparison.OrdinalIgnoreCase)) return "Shape layer";
        if (string.Equals(kind, "Adjustment layer", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "Adjustment", StringComparison.OrdinalIgnoreCase)) return "Adjustment layer";
        if (string.Equals(kind, "Smart object", StringComparison.OrdinalIgnoreCase)) return "Smart object";
        return "Layer";
    }

    private static bool HasValidApertureMap(FrameSlotBounds? bounds, byte[]? apertureMask, int? documentWidth, int? documentHeight)
    {
        if (bounds is not { } rectangle
            || rectangle.Width <= 0 || rectangle.Height <= 0
            || documentWidth is not > 0 || documentHeight is not > 0
            || rectangle.X < 0 || rectangle.Y < 0
            || (long)rectangle.X + rectangle.Width > documentWidth.Value
            || (long)rectangle.Y + rectangle.Height > documentHeight.Value
            || apertureMask is not { Length: > 0 })
            return false;

        var expectedLength = (long)rectangle.Width * rectangle.Height;
        return apertureMask.LongLength == expectedLength
            && apertureMask.Any(sample => sample == 255)
            && apertureMask.All(sample => sample is 0 or 255);
    }

    private static string SafeInspectionFailure(FramePsdReadFailureKind failureKind) => failureKind switch
    {
        FramePsdReadFailureKind.SourceNotFound => "File PSD lokal tidak ditemukan. File sumber tidak diubah.",
        FramePsdReadFailureKind.AccessDenied => "Akses ke file PSD lokal ditolak. File sumber tidak diubah.",
        FramePsdReadFailureKind.UnsupportedFeature => "Fitur pada PSD ini belum didukung pembaca. File sumber tidak diubah.",
        FramePsdReadFailureKind.ReaderCouldNotInterpretInput => "PSD ini belum dapat dibaca oleh pembaca yang tersedia. Ini tidak membuktikan file rusak.",
        _ => "Pemeriksaan PSD gagal. File sumber tidak diubah; hasil ini tidak membuktikan file rusak."
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ClearInspectionState()
    {
        IsManualFrameDrawing = false;
        IsFallbackPreviewMode = false;
        _workflowHints = null;
        ActiveReaderLabel = string.Empty;
        OnPropertyChanged(nameof(ActiveReaderLabel));
        SelectSlot(null);
        Slots.Clear();
        _templatePreviewPng = null;
        _documentWidth = null;
        _documentHeight = null;
        _layerTreeSummary = null;
        LayerTreeRows.Clear();
        OnPropertyChanged(nameof(LayerTreeSummaryText));
        OnPropertyChanged(nameof(WorkflowStructureNotice));
        OnPropertyChanged(nameof(IsFallbackPreviewMode));
        OnPropertyChanged(nameof(ActiveReaderLabel));
        OnPropertyChanged(nameof(TemplatePreviewPng));
        OnPropertyChanged(nameof(HasTemplatePreview));
        OnPropertyChanged(nameof(ShowDetectedAreaList));
        OnPropertyChanged(nameof(HasValidPhotoSlotSetup));
        OnPropertyChanged(nameof(ShowPhotoSlotSetupNotice));
        OnPropertyChanged(nameof(EmptyInspectorText));
        OnPropertyChanged(nameof(CanPlaceSelectedSlot));
        OnPropertyChanged(nameof(CanAdjustSelectedSlot));
        OnPropertyChanged(nameof(IsImageSlotSelected));
        OnPropertyChanged(nameof(CanAssignSelectedPhoto));
        OnPropertyChanged(nameof(HasManualPhotoFrame));
        OnPropertyChanged(nameof(CanUseManualFrameTool));
        OnPropertyChanged(nameof(CanManageSourcePhotos));
        OnPropertyChanged(nameof(DocumentDimensions));
        OnPropertyChanged(nameof(DocumentWidth));
        OnPropertyChanged(nameof(DocumentHeight));
        OnPropertyChanged(nameof(CanExportPngCopies));
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
