namespace FrameStudio.Engine;

public enum FramePsdSlotType
{
    Photo,
    Text,
    Logo
}

/// <summary>Distinguishes reader failure from a claim that an input file is corrupt.</summary>
public enum FramePsdReadFailureKind
{
    None,
    SourceNotFound,
    AccessDenied,
    UnsupportedFeature,
    ReaderCouldNotInterpretInput,
    ReaderFailure
}

/// <summary>
/// Honest engine-level feature flags. Read support is best-effort per document; an attempted PSD/PSB
/// read is not a guarantee that every Photoshop feature or tagged block is supported.
/// </summary>
public sealed record FramePsdEngineCapabilities(
    string ReaderName,
    string ReaderVersion,
    string ReaderLicense,
    bool CanAttemptPsdRead,
    bool CanAttemptPsbRead,
    bool CanInspectLayerTreeBestEffort,
    bool CanExportSupportedStoredCompositePng,
    bool CanEditPhotoLayers,
    bool CanEditTextLayers,
    bool CanReplaceLogoLayers,
    bool CanWriteLayeredPsd)
{
    public FramePsdOptionalBackendCapabilities? OptionalFallback { get; init; }

    public static FramePsdEngineCapabilities Current { get; } = new(
        "PsdSharp",
        "1.2.0",
        "MIT",
        CanAttemptPsdRead: true,
        CanAttemptPsbRead: true,
        CanInspectLayerTreeBestEffort: true,
        CanExportSupportedStoredCompositePng: true,
        CanEditPhotoLayers: false,
        CanEditTextLayers: false,
        CanReplaceLogoLayers: false,
        CanWriteLayeredPsd: false);
}

/// <summary>Engine-side equivalent of the UI PsdSlotDescriptor contract.</summary>
public sealed record FramePsdSlotDescriptor(
    string Id,
    string DisplayName,
    FramePsdSlotType Type,
    string LayerName,
    string? CurrentValue = null,
    FrameBounds? Bounds = null,
    bool IsPlacementReady = false);

/// <summary>A sanitized, descriptive layer node; source names and text values are never included.</summary>
public sealed record FramePsdLayerSummary(
    string Id,
    string DisplayName,
    string Kind,
    bool IsGroup,
    bool IsVisible,
    byte Opacity,
    FrameBounds Bounds,
    bool HasPixelData,
    bool HasMask,
    bool HasVectorMask,
    bool HasEffects,
    IReadOnlyList<FramePsdLayerSummary> Children)
{
    public bool HasClipping { get; init; }
}

public sealed record FramePsdLayerTreeSummary(
    int LayerCount,
    int GroupCount,
    int TextLayerCount,
    int MaskCount,
    int VectorMaskCount,
    int EffectLayerCount,
    IReadOnlyList<FramePsdLayerSummary> Roots);

/// <summary>Engine-side equivalent of the UI PsdTemplateInspection contract.</summary>
public sealed record FramePsdTemplateInspection(
    bool Success,
    string Message,
    IReadOnlyList<FramePsdSlotDescriptor> Slots,
    FramePsdReadFailureKind FailureKind = FramePsdReadFailureKind.None,
    FramePsdEngineCapabilities? EngineCapabilities = null,
    byte[]? FlattenedPreviewPng = null,
    int? DocumentWidth = null,
    int? DocumentHeight = null,
    FramePsdLayerTreeSummary? LayerTree = null,
    string? ActiveReaderName = null);

/// <summary>Engine-side equivalent of the UI PsdExportRequest contract.</summary>
public sealed record FramePsdExportRequest(
    string MasterPath,
    string SourceFolder,
    string OutputFolder,
    IReadOnlyDictionary<string, string> SlotAssignments);

/// <summary>Engine-side equivalent of the UI PsdExportResult contract.</summary>
public sealed record FramePsdExportResult(
    bool Success,
    string Message,
    string? OutputPath = null,
    FramePsdReadFailureKind FailureKind = FramePsdReadFailureKind.None);

/// <summary>
/// Async bridge for the Avalonia template workflow. It performs best-effort read-only inspection and
/// only exports a stored composite when a validated photo target exists. It never edits or saves layered PSDs.
/// </summary>
public sealed class PsdTemplateEngineAdapter
{
    private static readonly HashSet<string> PhotoMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "photo", "foto", "portrait", "slot"
    };

    private static readonly HashSet<string> TextMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "name", "nama", "address", "alamat", "school", "sekolah", "class", "kelas", "student", "siswa"
    };

    private static readonly HashSet<string> LogoMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "logo", "crest", "emblem", "badge", "lambang", "lencana"
    };

    private readonly PsdDocumentReader _reader;

    public PsdTemplateEngineAdapter(PsdDocumentReader? reader = null)
    {
        _reader = reader ?? new PsdDocumentReader();
    }

    /// <summary>Indicates that the adapter is present, not that every PSD feature is supported.</summary>
    public bool IsAvailable => true;

    /// <summary>Features this build can safely claim independently of any one file.</summary>
    public FramePsdEngineCapabilities Capabilities => _reader.Capabilities;

    public Task<FramePsdTemplateInspection> InspectAsync(
        string masterPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Inspect(masterPath, cancellationToken), cancellationToken);
    }

    public Task<FramePsdExportResult> ExportCopyAsync(
        FramePsdExportRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExportCopy(request, cancellationToken), cancellationToken);
    }

    private FramePsdTemplateInspection Inspect(string masterPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var document = _reader.Read(masterPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var slotCandidates = EnumerateLayers(document.Layers)
                .Select(layer => TryMapSlot(layer, document.Width, document.Height))
                .Where(slot => slot is not null)
                .Select(slot => slot!)
                .ToArray();
            var slots = SanitizeSlots(slotCandidates);
            var layerTree = BuildLayerTreeSummary(document.Layers);
            byte[]? flattenedPreviewPng = null;
            if (document.CompositePreview is not null)
            {
                using var previewStream = new MemoryStream();
                document.WriteCompositePreviewPng(previewStream);
                flattenedPreviewPng = previewStream.ToArray();
            }

            var message = $"Read-only stored composite: {document.Width}×{document.Height} {document.ColorMode}; {layerTree.LayerCount} layer nodes, {layerTree.GroupCount} groups. Layer metadata is best-effort; text values and layer names are not exposed.";
            return new FramePsdTemplateInspection(
                true,
                message,
                Array.AsReadOnly(slots),
                FailureKind: FramePsdReadFailureKind.None,
                EngineCapabilities: Capabilities,
                FlattenedPreviewPng: flattenedPreviewPng,
                DocumentWidth: document.Width,
                DocumentHeight: document.Height,
                LayerTree: layerTree,
                ActiveReaderName: document.ReaderBackend);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            var failureKind = ClassifyReadFailure(exception);
            return new FramePsdTemplateInspection(
                false,
                DescribeReadFailure(exception),
                Array.Empty<FramePsdSlotDescriptor>(),
                failureKind,
                Capabilities);
        }
    }

    private FramePsdExportResult ExportCopy(FramePsdExportRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return new FramePsdExportResult(false, "An export request is required.");
        }

        if (string.IsNullOrWhiteSpace(request.MasterPath) || string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            return new FramePsdExportResult(false, "MasterPath and OutputFolder are required.");
        }

        if (request.SlotAssignments is null)
        {
            return new FramePsdExportResult(false, "SlotAssignments must be provided.");
        }

        if (request.SlotAssignments.Count > 0)
        {
            return new FramePsdExportResult(
                false,
                "Photo, text, and logo assignments are not supported by this read-only engine. No output was written and the master PSD was not changed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        FramePsdDocument document;
        try
        {
            document = _reader.Read(request.MasterPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new FramePsdExportResult(
                false,
                DescribeReadFailure(exception),
                FailureKind: ClassifyReadFailure(exception));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var hasValidatedPhotoSlot = EnumerateLayers(document.Layers)
            .Any(layer => TryMapSlot(layer, document.Width, document.Height) is
            {
                Type: FramePsdSlotType.Photo,
                IsPlacementReady: true
            });
        if (!hasValidatedPhotoSlot)
        {
            return new FramePsdExportResult(
                false,
                "PNG export is disabled because no photo slot with valid bounds and mask or clipping evidence was found; no output was written.");
        }

        if (document.CompositePreview is null)
        {
            return new FramePsdExportResult(false, "No supported stored composite preview is available; no output was written.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var outputFolder = Path.GetFullPath(request.OutputFolder);
            Directory.CreateDirectory(outputFolder);
            var baseName = Path.GetFileNameWithoutExtension(request.MasterPath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "frame-studio";
            }

            for (var suffix = 1; suffix <= 10_000; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = suffix == 1
                    ? $"{baseName}-flattened.png"
                    : $"{baseName}-flattened-{suffix}.png";
                var outputPath = Path.Combine(outputFolder, fileName);
                try
                {
                    document.SaveCompositePreviewPng(outputPath);
                    return new FramePsdExportResult(
                        true,
                        "Wrote a separate flattened PNG copy of the PSD's stored composite. Slot assignments were not applied; the master PSD was left unchanged.",
                        outputPath);
                }
                catch (IOException) when (File.Exists(outputPath))
                {
                    // Choose another new name; SaveCompositePreviewPng itself never overwrites.
                }
            }

            return new FramePsdExportResult(false, "Could not find an unused output filename after 10,000 attempts.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new FramePsdExportResult(false, "Flattened preview export failed; no layered PSD was written.");
        }
    }

    private static FramePsdReadFailureKind ClassifyReadFailure(Exception exception)
    {
        var cause = exception is FramePsdReaderFallbackException fallback
            ? Unwrap(fallback.FallbackException)
            : Unwrap(exception);
        return cause switch
        {
            FileNotFoundException or DirectoryNotFoundException => FramePsdReadFailureKind.SourceNotFound,
            UnauthorizedAccessException => FramePsdReadFailureKind.AccessDenied,
            NotSupportedException => FramePsdReadFailureKind.UnsupportedFeature,
            InvalidDataException or EndOfStreamException => FramePsdReadFailureKind.ReaderCouldNotInterpretInput,
            _ => FramePsdReadFailureKind.ReaderFailure
        };
    }

    private static string DescribeReadFailure(Exception exception)
    {
        if (exception is FramePsdInputLimitException)
        {
            return "The source exceeds the engine's configured input-size limit; no output was written.";
        }

        if (exception is FramePsdReaderFallbackException)
        {
            return "PsdSharp and the optional psd-tools reader could not read the selected document. Verify the Linux x64 runtime prerequisites (Python 3.11+ and psd-tools 1.26.0). This does not establish that the source file is corrupt.";
        }

        return ClassifyReadFailure(exception) switch
        {
            FramePsdReadFailureKind.SourceNotFound => "The source PSD/PSB was not found; no output was written.",
            FramePsdReadFailureKind.AccessDenied => "Access to the source PSD/PSB was denied; no output was written.",
            FramePsdReadFailureKind.UnsupportedFeature => "The configured PSD reader does not support part of this document. This does not establish that the source file is corrupt; no output was written.",
            FramePsdReadFailureKind.ReaderCouldNotInterpretInput => "The configured PSD readers could not interpret this document. This does not establish that the source file is corrupt.",
            _ => "PSD inspection failed without changing the source; this reader result does not establish that the source file is corrupt. Check the reader capability status and try again."
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        while (true)
        {
            if (exception is System.Reflection.TargetInvocationException { InnerException: not null } invocation)
            {
                exception = invocation.InnerException!;
                continue;
            }

            if (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
            {
                exception = aggregate.InnerExceptions[0];
                continue;
            }

            return exception!;
        }
    }

    private static FramePsdSlotDescriptor? TryMapSlot(FramePsdLayer layer, int documentWidth, int documentHeight)
    {
        var tokens = SplitName(layer.Name);
        FramePsdSlotType? type = null;

        if (tokens.Any(LogoMarkers.Contains))
        {
            type = FramePsdSlotType.Logo;
        }
        else if (layer.SlotHint.HasValue || tokens.Any(PhotoMarkers.Contains))
        {
            type = FramePsdSlotType.Photo;
        }
        else if (tokens.Any(TextMarkers.Contains))
        {
            type = FramePsdSlotType.Text;
        }

        if (!type.HasValue)
            return null;

        var isImageTarget = type is FramePsdSlotType.Photo or FramePsdSlotType.Logo;
        if (isImageTarget && !HasValidatedImageBoundary(layer, documentWidth, documentHeight))
            return null;
        if (type == FramePsdSlotType.Text && !HasValidBounds(layer.Bounds, documentWidth, documentHeight))
            return null;

        return new FramePsdSlotDescriptor(
            layer.Id,
            layer.Name,
            type.Value,
            layer.Name,
            Bounds: layer.Bounds,
            IsPlacementReady: isImageTarget);
    }

    private static bool HasValidatedImageBoundary(FramePsdLayer layer, int documentWidth, int documentHeight) =>
        HasValidBounds(layer.Bounds, documentWidth, documentHeight)
        && (layer.HasMask || layer.HasVectorMask || layer.HasClipping);

    private static bool HasValidBounds(FrameBounds bounds, int documentWidth, int documentHeight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || documentWidth <= 0 || documentHeight <= 0)
            return false;

        var right = (long)bounds.X + bounds.Width;
        var bottom = (long)bounds.Y + bounds.Height;
        return bounds.X >= 0 && bounds.Y >= 0 && right <= documentWidth && bottom <= documentHeight;
    }

    private static FramePsdSlotDescriptor[] SanitizeSlots(IReadOnlyList<FramePsdSlotDescriptor> candidates)
    {
        var counts = new Dictionary<FramePsdSlotType, int>();
        return candidates.Select(candidate =>
        {
            counts.TryGetValue(candidate.Type, out var count);
            count++;
            counts[candidate.Type] = count;
            var label = $"{candidate.Type} candidate {count}";
            return candidate with { DisplayName = label, LayerName = label, CurrentValue = null };
        }).ToArray();
    }

    private static FramePsdLayerTreeSummary BuildLayerTreeSummary(IReadOnlyList<FramePsdLayer> roots)
    {
        var kindCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var layerCount = 0;
        var groupCount = 0;
        var textLayerCount = 0;
        var maskCount = 0;
        var vectorMaskCount = 0;
        var effectLayerCount = 0;

        IReadOnlyList<FramePsdLayerSummary> BuildNodes(IReadOnlyList<FramePsdLayer> layers, string parentId)
        {
            return layers.Select((layer, index) =>
            {
                var id = string.IsNullOrEmpty(parentId) ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{parentId}.{index + 1}";
                var isGroup = layer.IsGroup || layer.Children.Count > 0 || IsKind(layer.Kind, "group");
                var kind = SafeKind(layer.Kind, isGroup);
                kindCounts.TryGetValue(kind, out var sameKindCount);
                sameKindCount++;
                kindCounts[kind] = sameKindCount;
                layerCount++;
                if (isGroup) groupCount++;
                if (string.Equals(kind, "Text layer", StringComparison.Ordinal)) textLayerCount++;
                if (layer.HasMask) maskCount++;
                if (layer.HasVectorMask) vectorMaskCount++;
                if (layer.HasEffects) effectLayerCount++;

                return new FramePsdLayerSummary(
                    id,
                    $"{kind} {sameKindCount}",
                    kind,
                    isGroup,
                    layer.IsVisible,
                    layer.Opacity,
                    layer.Bounds,
                    layer.HasPixelData,
                    layer.HasMask,
                    layer.HasVectorMask,
                    layer.HasEffects,
                    BuildNodes(layer.Children, id))
                {
                    HasClipping = layer.HasClipping
                };
            }).ToArray();
        }

        var sanitizedRoots = BuildNodes(roots, string.Empty);
        return new FramePsdLayerTreeSummary(layerCount, groupCount, textLayerCount, maskCount, vectorMaskCount, effectLayerCount, Array.AsReadOnly(sanitizedRoots.ToArray()));
    }

    private static string SafeKind(string? kind, bool isGroup)
    {
        if (isGroup) return "Group";
        if (IsKind(kind, "type") || IsKind(kind, "text")) return "Text layer";
        if (IsKind(kind, "pixel")) return "Pixel layer";
        if (IsKind(kind, "shape")) return "Shape layer";
        if (IsKind(kind, "adjustment")) return "Adjustment layer";
        if (IsKind(kind, "smartobject") || IsKind(kind, "smart object")) return "Smart object";
        return "Layer";
    }

    private static bool IsKind(string? candidate, string expected) => string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<FramePsdLayer> EnumerateLayers(IEnumerable<FramePsdLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in EnumerateLayers(layer.Children))
            {
                yield return child;
            }
        }
    }

    private static string[] SplitName(string name)
    {
        return name.Split(
                new[] { ' ', '\t', '\r', '\n', '-', '_', '[', ']', '(', ')', ':', '/', '\\', '.' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();
    }
}
