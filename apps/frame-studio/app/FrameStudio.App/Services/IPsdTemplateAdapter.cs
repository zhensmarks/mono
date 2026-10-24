namespace FrameStudio.App.Services;

public enum PsdSlotType
{
    Photo,
    Text,
    Logo
}

public sealed record PsdSlotDescriptor(string Id, string DisplayName, PsdSlotType Type, string LayerName, string? CurrentValue = null);

public sealed record PsdTemplateInspection(
    bool Success,
    string Message,
    IReadOnlyList<PsdSlotDescriptor> Slots,
    byte[]? FlattenedPreviewPng = null,
    int? DocumentWidth = null,
    int? DocumentHeight = null);

public sealed record PsdExportRequest(string MasterPath, string SourceFolder, string OutputFolder, IReadOnlyDictionary<string, string> SlotAssignments);

public sealed record PsdExportResult(bool Success, string Message, string? OutputPath = null);

/// <summary>
/// Boundary between the Avalonia workflow and a PSD document engine.
/// The current engine is read-only; it may only create a separate flattened composite PNG.
/// </summary>
public interface IPsdTemplateAdapter
{
    bool IsAvailable { get; }
    Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default);
    Task<PsdExportResult> ExportCopyAsync(PsdExportRequest request, CancellationToken cancellationToken = default);
}
