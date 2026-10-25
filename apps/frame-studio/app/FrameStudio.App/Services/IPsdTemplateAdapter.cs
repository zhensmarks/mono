using FrameStudio.Engine;

namespace FrameStudio.App.Services;

public enum PsdSlotType
{
    Photo,
    Text,
    Logo
}

/// <summary>Document-space layer bounds; image placement still requires the exact binary aperture below.</summary>
public sealed record PsdSlotBounds(int X, int Y, int Width, int Height);

public sealed record PsdSlotDescriptor(
    string Id,
    string DisplayName,
    PsdSlotType Type,
    string LayerName,
    string? CurrentValue = null,
    PsdSlotBounds? Bounds = null,
    // True for image slots only when the exact binary aperture matches the bounds and renderer contract.
    bool IsPlacementReady = false,
    // Exact per-pixel 0/255 coverage; null means the mask is missing or unsupported.
    byte[]? ApertureMask = null);
public sealed record PsdTemplateInspection(
    bool Success,
    string Message,
    IReadOnlyList<PsdSlotDescriptor> Slots,
    byte[]? FlattenedPreviewPng = null,
    int? DocumentWidth = null,
    int? DocumentHeight = null,
    FramePsdLayerTreeSummary? LayerTree = null,
    FramePsdEngineCapabilities? EngineCapabilities = null,
    string? ActiveReaderName = null,
    FramePsdReadFailureKind FailureKind = FramePsdReadFailureKind.None)
{
    public FramePsdWorkflowHints? WorkflowHints { get; init; }
}

/// <summary>
/// Boundary between the Avalonia workflow and the read-only PSD document inspector.
/// Raster PNG composition is handled separately and never edits layered PSD data.
/// </summary>
public interface IPsdTemplateAdapter
{
    bool IsAvailable { get; }
    Task<PsdTemplateInspection> InspectAsync(string masterPath, CancellationToken cancellationToken = default);
}
