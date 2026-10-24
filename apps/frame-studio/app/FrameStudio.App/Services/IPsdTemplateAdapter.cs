namespace FrameStudio.App.Services;

public enum PsdSlotType
{
    Photo,
    Text,
    Logo
}

public sealed record PsdSlotDescriptor(string Id, string DisplayName, PsdSlotType Type, string LayerName, string? CurrentValue = null);

public sealed record PsdTemplateInspection(bool Success, string Message, IReadOnlyList<PsdSlotDescriptor> Slots);

public sealed record PsdExportRequest(string MasterPath, string SourceFolder, string OutputFolder, IReadOnlyDictionary<string, string> SlotAssignments);

public sealed record PsdExportResult(bool Success, string Message, string? OutputPath = null);

/// <summary>
/// Boundary between the Avalonia workflow and a PSD document engine.
/// The UI never writes a PSD directly; implementations must create a new output copy.
/// </summary>
public interface IPsdTemplateAdapter
{
    bool IsAvailable { get; }
    Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default);
    Task<PsdExportResult> ExportCopyAsync(PsdExportRequest request, CancellationToken cancellationToken = default);
}
