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
    private double _zoom = 1;
    private double _rotation;
    private double _offsetX;
    private double _offsetY;
    private bool _isFitMode;
    private bool _isInspecting;
    private bool _isExporting;
    private bool _isFallbackPreviewMode;
    private int _inspectionGeneration;

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
            ? $"{RuntimePlatformLabel} · PsdSharp 1.2.0 · read-only · layered PSD disabled"
            : $"{RuntimePlatformLabel} · PSD reader unavailable · layered PSD disabled";
    public string EngineDetails => _psdAdapter is PsdTemplateEngineUiAdapter uiAdapter
        ? uiAdapter.CapabilityDetails
        : $"Runtime platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). Layered PSD writing is disabled; supported output is a separate PNG.";
    public string ExportNotice => IsFallbackPreviewMode
        ? "PNG output is a separate copy of the stored composite; no photo, text, logo, mask, effect, or PSD-layer edits are applied."
        : "Layered PSD export is unavailable; only separate raster PNG output is supported. The PSD master remains read-only.";
    public string MasterPath { get => _masterPath; set { _masterPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMaster)); OnPropertyChanged(nameof(MasterPathDisplayText)); OnPropertyChanged(nameof(CanExportPngCopies)); } }
    public string MasterPathDisplayText => HasMaster ? "Local PSD master selected" : string.Empty;
    public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }
    public string OutputPath { get => _outputPath; set { _outputPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportPngCopies)); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string TemplateStatus { get => _templateStatus; set { _templateStatus = value; OnPropertyChanged(); } }
    public string TemplatePreviewNotice { get => _templatePreviewNotice; private set { _templatePreviewNotice = value; OnPropertyChanged(); } }
    public bool HasMaster => !string.IsNullOrWhiteSpace(MasterPath);
    public bool HasTemplatePreview => _templatePreviewPng is { Length: > 0 };
    public bool HasSelectedPhoto => SelectedPhoto is not null;
    public bool HasPreviewImage => SelectedPhoto?.Image is not null;
    public bool HasPreviewUnavailable => SelectedPhoto is not null && SelectedPhoto.Image is null;
    public bool HasSelectedLogo => SelectedLogo is not null;
    public bool HasTextCandidateSelected => SelectedSlot?.IsText == true;
    public bool IsFallbackPreviewMode { get => _isFallbackPreviewMode; private set { if (_isFallbackPreviewMode == value) return; _isFallbackPreviewMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditPhotoTextLogo)); OnPropertyChanged(nameof(ExportNotice)); OnPropertyChanged(nameof(EngineNotice)); OnPropertyChanged(nameof(EngineDetails)); OnPropertyChanged(nameof(LayerTreeSummaryText)); } }
    public bool CanEditPhotoTextLogo => !IsFallbackPreviewMode && !IsInspecting && !IsExporting;
    public string LayerTreeSummaryText => _layerTreeSummary is { } summary
        ? $"{summary.LayerCount} layers · {summary.GroupCount} groups · {summary.TextLayerCount} text · {summary.MaskCount} masks · {summary.VectorMaskCount} vector masks · {summary.EffectLayerCount} effects"
        : "Layer details are not available.";
    public string ActiveReaderLabel { get; private set; } = "";
    public int DocumentWidth => _documentWidth ?? 0;
    public int DocumentHeight => _documentHeight ?? 0;
    public bool IsInspecting { get => _isInspecting; private set { _isInspecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportPngCopies)); OnPropertyChanged(nameof(CanEditPhotoTextLogo)); } }
    public bool IsExporting { get => _isExporting; private set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportPngCopies)); OnPropertyChanged(nameof(CanEditPhotoTextLogo)); } }
    public string DocumentDimensions => _documentWidth is { } width && _documentHeight is { } height
        ? $"{width:N0} × {height:N0} px"
        : "";
    public byte[]? TemplatePreviewPng => _templatePreviewPng;
    public SourcePhoto? SelectedPhoto { get => _selectedPhoto; set { _selectedPhoto = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedPhoto)); OnPropertyChanged(nameof(HasPreviewImage)); OnPropertyChanged(nameof(HasPreviewUnavailable)); } }
    public SourcePhoto? SelectedLogo => Slots.FirstOrDefault(slot => slot.IsLogo)?.Photo;
    public FrameSlot? SelectedSlot { get => _selectedSlot; private set { _selectedSlot = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTextCandidateSelected)); OnPropertyChanged(nameof(IsImageSlotSelected)); OnPropertyChanged(nameof(Zoom)); OnPropertyChanged(nameof(ZoomLabel)); OnPropertyChanged(nameof(Rotation)); OnPropertyChanged(nameof(RotationLabel)); OnPropertyChanged(nameof(OffsetX)); OnPropertyChanged(nameof(OffsetY)); OnPropertyChanged(nameof(IsFitMode)); } }
    public double Zoom { get => SelectedSlot?.Zoom ?? _zoom; set { if (IsFallbackPreviewMode) return; var zoom = Math.Clamp(value, 0.5, 3); if (SelectedSlot is { } slot) slot.Zoom = zoom; else _zoom = zoom; OnPropertyChanged(); OnPropertyChanged(nameof(ZoomLabel)); } }
    public double Rotation { get => SelectedSlot?.Rotation ?? _rotation; set { if (IsFallbackPreviewMode) return; var rotation = value % 360; if (SelectedSlot is { } slot) slot.Rotation = rotation; else _rotation = rotation; OnPropertyChanged(); OnPropertyChanged(nameof(RotationLabel)); } }
    public double OffsetX { get => SelectedSlot?.OffsetX ?? _offsetX; set { if (IsFallbackPreviewMode) return; if (SelectedSlot is { } slot) slot.OffsetX = value; else _offsetX = value; OnPropertyChanged(); } }
    public double OffsetY { get => SelectedSlot?.OffsetY ?? _offsetY; set { if (IsFallbackPreviewMode) return; if (SelectedSlot is { } slot) slot.OffsetY = value; else _offsetY = value; OnPropertyChanged(); } }
    public bool IsFitMode { get => SelectedSlot?.IsFitMode ?? _isFitMode; private set { if (IsFallbackPreviewMode) return; if (SelectedSlot is { } slot) slot.IsFitMode = value; else _isFitMode = value; OnPropertyChanged(); } }
    public bool IsImageSlotSelected => SelectedSlot is { } slot && (slot.IsPhoto || slot.IsLogo);
    public string ZoomLabel => $"{Zoom:P0}";
    public string RotationLabel => $"{Rotation:0.#}°";
    public bool CanExportPngCopies => PsdEngineReady && HasMaster && HasTemplatePreview && !IsInspecting && !IsExporting && !string.IsNullOrWhiteSpace(OutputPath);

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

    public void SelectSlot(FrameSlot? slot)
    {
        if (IsFallbackPreviewMode)
            return;

        foreach (var item in Slots)
            item.IsSelected = ReferenceEquals(item, slot);
        SelectedSlot = slot;
        if (slot?.Photo is not null)
            SelectedPhoto = slot.Photo;
    }

    public void AddSourcePhoto(SourcePhoto photo)
    {
        if (!CanEditPhotoTextLogo || IsFallbackPreviewMode)
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
        if (!CanEditPhotoTextLogo || slot is null || (!slot.IsPhoto && !slot.IsLogo) || SelectedPhoto is null)
            return false;

        slot.Photo = SelectedPhoto;
        slot.Zoom = 1;
        slot.Rotation = 0;
        slot.OffsetX = 0;
        slot.OffsetY = 0;
        slot.IsFitMode = false;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        SelectSlot(slot);
        Status = $"{SelectedPhoto.FileName} ditempatkan pada {slot.Label}; placement ikut PNG ekspor, PSD master tidak diubah.";
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
        TemplatePreviewNotice = "Membaca composite tersimpan dari PSD…";
        Status = "Local PSD selected. Checking the stored composite and read-only layer metadata.";
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
        Status = "Inspecting the local PSD with bounded read-only readers…";

        try
        {
            var result = await _psdAdapter.InspectAsync(masterPath, cancellationToken);
            if (generation != _inspectionGeneration)
                return;

            if (!result.Success)
            {
                TemplateStatus = "Pemeriksaan gagal";
                TemplatePreviewNotice = "Composite tidak tersedia. Periksa pesan status dan pilih file PSD yang didukung.";
                Status = SafeInspectionFailure(result.FailureKind);
                OnPropertyChanged(nameof(EngineNotice));
                OnPropertyChanged(nameof(EngineDetails));
                return;
            }

            var usedFallback = string.Equals(result.ActiveReaderName, "psd-tools desktop sidecar", StringComparison.Ordinal);
            IsFallbackPreviewMode = usedFallback;
            ActiveReaderLabel = usedFallback ? "psd-tools 1.26.0 (local sidecar)" : "PsdSharp 1.2.0";
            OnPropertyChanged(nameof(ActiveReaderLabel));
            _layerTreeSummary = result.LayerTree;
            LayerTreeRows.Clear();
            if (usedFallback && result.LayerTree is { } tree)
                AddLayerRows(tree.Roots);

            if (!usedFallback)
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
                    var safeLabel = $"{kind} candidate {typeCount}";
                    var safeId = $"slot/{++slotIndex}";
                    FrameSlotBounds? bounds = descriptor.Bounds is { } rectangle
                        ? new FrameSlotBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)
                        : null;
                    var boundsHint = bounds is { } estimated
                        ? $" · estimated box {estimated.Width:N0} × {estimated.Height:N0} px (not a mask)"
                        : " · bounds unavailable";
                    Slots.Add(new FrameSlot(safeId, safeLabel, kind, $"Best-effort candidate{boundsHint}", null, bounds));
                }
            }
            else
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
            OnPropertyChanged(nameof(CanExportPngCopies));

            TemplateStatus = usedFallback
                ? $"{LayerTreeSummaryText} · {_documentWidth:N0} × {_documentHeight:N0} px"
                : _documentWidth is { } width && _documentHeight is { } height
                    ? $"{Slots.Count} anonymous candidates · {width:N0} × {height:N0} px"
                    : $"{Slots.Count} anonymous candidates";
            TemplatePreviewNotice = HasTemplatePreview
                ? usedFallback
                    ? "Stored merged composite from the local psd-tools reader. Read-only; the layer tree is not recomposed."
                    : "Stored composite from PSD. Read-only preview; layers are not recomposed."
                : "Metadata dibaca, tetapi tidak ada composite RGB yang didukung untuk pratinjau.";
            SelectSlot(usedFallback ? null : Slots.FirstOrDefault());
            Status = usedFallback
                ? "Read-only stored composite and anonymous layer tree loaded. Photo, text, and logo editing are disabled."
                : "Read-only PSD inspection completed. The stored composite and anonymous slot hints are available.";
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
                TemplatePreviewNotice = "PSD tidak dapat diperiksa. Periksa file dan coba lagi.";
                Status = "PSD inspection failed without changing the source. Check the reader runtime and try again.";
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
        if (IsFallbackPreviewMode)
            return;

        IsFitMode = false;
        Zoom = 1;
        Rotation = 0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetPreviewMode(bool showWholePhoto)
    {
        if (IsFallbackPreviewMode)
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

    private static string SafeInspectionFailure(FramePsdReadFailureKind failureKind) => failureKind switch
    {
        FramePsdReadFailureKind.SourceNotFound => "The selected local PSD was not found. No file was changed.",
        FramePsdReadFailureKind.AccessDenied => "Access to the selected local PSD was denied. No file was changed.",
        FramePsdReadFailureKind.UnsupportedFeature => "The configured reader does not support part of this PSD. No file was changed.",
        FramePsdReadFailureKind.ReaderCouldNotInterpretInput => "The configured readers could not interpret this PSD; this does not establish that it is corrupt. On Linux x64, verify Python 3.11+ and psd-tools==1.26.0.",
        _ => "PSD inspection failed without changing the source; this reader result does not establish that the file is corrupt. Check the reader capability status and try again."
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ClearInspectionState()
    {
        IsFallbackPreviewMode = false;
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
        OnPropertyChanged(nameof(IsFallbackPreviewMode));
        OnPropertyChanged(nameof(ActiveReaderLabel));
        OnPropertyChanged(nameof(TemplatePreviewPng));
        OnPropertyChanged(nameof(HasTemplatePreview));
        OnPropertyChanged(nameof(DocumentDimensions));
        OnPropertyChanged(nameof(DocumentWidth));
        OnPropertyChanged(nameof(DocumentHeight));
        OnPropertyChanged(nameof(CanExportPngCopies));
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
