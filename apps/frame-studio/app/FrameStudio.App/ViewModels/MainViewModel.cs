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
    private SourcePhoto? _selectedPhoto;
    private FrameSlot? _selectedSlot;
    private double _zoom = 1;
    private double _rotation;
    private double _offsetX;
    private double _offsetY;
    private bool _isFitMode;

    public MainViewModel(IPsdTemplateAdapter? psdAdapter = null)
    {
        _psdAdapter = psdAdapter ?? new PreviewOnlyPsdTemplateAdapter();
        Slots = new ObservableCollection<FrameSlot>
        {
            new("xl", "XL", SlotKind.Photo, "Slot foto utama"),
            new("s", "S", SlotKind.Photo, "Slot foto kedua"),
            new("name", "Nama", SlotKind.Text, "Text layer", "Contoh Nama Siswa"),
            new("address", "Alamat", SlotKind.Text, "Text layer", "Jl. Merdeka No. 12"),
            new("school-logo", "Logo sekolah", SlotKind.Logo, "Image layer")
        };
        SelectSlot(Slots[0]);
    }

    public ObservableCollection<FrameSlot> Slots { get; }
    public ObservableCollection<SourcePhoto> SourcePhotos { get; } = new();
    public bool PsdEngineReady => _psdAdapter.IsAvailable;
    public string EngineNotice => PsdEngineReady
        ? "Mesin PSD tersedia."
        : "Mode prototipe: PSD belum dibaca atau diubah. Isi slot dan kanvas di bawah adalah contoh UI.";
    public string ExportNotice => "Ekspor PSD belum aktif sampai mesin PSD tersambung.";
    public string PreviewStudentName => "Contoh Nama Siswa";
    public string PreviewAddress => "Jl. Merdeka No. 12";
    public string MasterPath { get => _masterPath; set { _masterPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMaster)); } }
    public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }
    public string OutputPath { get => _outputPath; set { _outputPath = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string TemplateStatus { get => _templateStatus; set { _templateStatus = value; OnPropertyChanged(); } }
    public bool HasMaster => !string.IsNullOrWhiteSpace(MasterPath);
    public SourcePhoto? SelectedPhoto { get => _selectedPhoto; set { _selectedPhoto = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedPhoto)); OnPropertyChanged(nameof(HasPreviewImage)); OnPropertyChanged(nameof(HasPreviewUnavailable)); } }
    public bool HasSelectedPhoto => SelectedPhoto is not null;
    public bool HasPreviewImage => SelectedPhoto?.Thumbnail is not null;
    public bool HasPreviewUnavailable => SelectedPhoto is not null && SelectedPhoto.Thumbnail is null;
    public SourcePhoto? SelectedLogo => Slots.FirstOrDefault(slot => slot.IsLogo)?.Photo;
    public bool HasSelectedLogo => SelectedLogo is not null;
    public FrameSlot? SelectedSlot { get => _selectedSlot; private set { _selectedSlot = value; OnPropertyChanged(); } }
    public double Zoom { get => _zoom; set { _zoom = Math.Clamp(value, 0.5, 3); OnPropertyChanged(); } }
    public double Rotation { get => _rotation; set { _rotation = value % 360; OnPropertyChanged(); } }
    public double OffsetX { get => _offsetX; set { _offsetX = value; OnPropertyChanged(); } }
    public double OffsetY { get => _offsetY; set { _offsetY = value; OnPropertyChanged(); } }
    public bool IsFitMode { get => _isFitMode; private set { _isFitMode = value; OnPropertyChanged(); } }
    public bool CanExport => false;

    public void SelectSlot(FrameSlot? slot)
    {
        foreach (var item in Slots)
            item.IsSelected = ReferenceEquals(item, slot);
        SelectedSlot = slot;
        if (slot?.IsPhoto == true)
        {
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
        Status = $"{photo.FileName} siap dipetakan ke slot.";
    }

    public bool AssignSelectedPhoto(FrameSlot? slot)
    {
        if (slot is null || (!slot.IsPhoto && !slot.IsLogo) || SelectedPhoto is null)
            return false;

        slot.Photo = SelectedPhoto;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
        SelectSlot(slot);
        Status = $"{SelectedPhoto.FileName} ditampilkan sebagai contoh di slot {slot.Label}. Ini belum mengubah PSD.";
        return true;
    }

    public void SetMaster(string path)
    {
        MasterPath = path;
        TemplateStatus = "PSD dipilih · belum dibaca";
        Status = "File master terpilih. Slot belum dibaca karena mesin PSD belum tersambung.";
    }

    public async Task InspectSelectedMasterAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MasterPath))
            return;

        var result = await _psdAdapter.InspectAsync(MasterPath, cancellationToken);
        if (!result.Success)
        {
            TemplateStatus = "PSD dipilih · belum dibaca";
            Status = result.Message;
            return;
        }

        SelectSlot(null);
        SelectedPhoto = null;
        Slots.Clear();
        foreach (var descriptor in result.Slots)
        {
            var kind = descriptor.Type switch
            {
                PsdSlotType.Photo => SlotKind.Photo,
                PsdSlotType.Text => SlotKind.Text,
                PsdSlotType.Logo => SlotKind.Logo,
                _ => SlotKind.Photo
            };
            Slots.Add(new FrameSlot(descriptor.Id, descriptor.DisplayName, kind, descriptor.LayerName, descriptor.CurrentValue));
        }

        TemplateStatus = $"{Slots.Count} slot terdeteksi";
        SelectSlot(Slots.FirstOrDefault());
        Status = result.Message;
        OnPropertyChanged(nameof(SelectedLogo));
        OnPropertyChanged(nameof(HasSelectedLogo));
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
        Status = "Antrean foto dikosongkan.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
