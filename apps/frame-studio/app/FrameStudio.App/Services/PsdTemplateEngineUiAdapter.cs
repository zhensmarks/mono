using FrameStudio.Engine;

namespace FrameStudio.App.Services;

/// <summary>Maps the UI contract to the read-only PSD engine; it never edits layered PSD data.</summary>
public sealed class PsdTemplateEngineUiAdapter : IPsdTemplateAdapter
{
    private readonly PsdTemplateEngineAdapter _engine;

    public PsdTemplateEngineUiAdapter(PsdTemplateEngineAdapter? engine = null)
    {
        _engine = engine ?? new PsdTemplateEngineAdapter();
    }

    public bool IsAvailable => _engine.IsAvailable;

    public async Task<PsdTemplateInspection> InspectAsync(
        string masterPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _engine.InspectAsync(masterPath, cancellationToken);
        var slots = result.Slots.Select(slot => new PsdSlotDescriptor(
            slot.Id,
            slot.DisplayName,
            slot.Type switch
            {
                FramePsdSlotType.Photo => PsdSlotType.Photo,
                FramePsdSlotType.Text => PsdSlotType.Text,
                FramePsdSlotType.Logo => PsdSlotType.Logo,
                _ => throw new ArgumentOutOfRangeException(nameof(slot.Type))
            },
            slot.LayerName,
            slot.CurrentValue,
            slot.Bounds is { } bounds
                ? new PsdSlotBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                : null)).ToArray();

        return new PsdTemplateInspection(
            result.Success,
            result.Message,
            Array.AsReadOnly(slots),
            result.FlattenedPreviewPng,
            result.DocumentWidth,
            result.DocumentHeight);
    }
}
