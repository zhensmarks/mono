namespace FrameStudio.App.Services;

/// <summary>
/// Explicit no-engine adapter used by the UI prototype. It never reads or writes PSD bytes.
/// </summary>
public sealed class PreviewOnlyPsdTemplateAdapter : IPsdTemplateAdapter
{
    public bool IsAvailable => false;

    public Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PsdTemplateInspection(
            Success: false,
            Message: "PSD belum dibaca. Mesin PSD belum tersambung; daftar slot yang tampil hanya contoh.",
            Slots: Array.Empty<PsdSlotDescriptor>()));
}
