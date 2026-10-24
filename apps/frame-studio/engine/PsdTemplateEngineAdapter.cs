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
    string? CurrentValue = null);

/// <summary>Engine-side equivalent of the UI PsdTemplateInspection contract.</summary>
public sealed record FramePsdTemplateInspection(
    bool Success,
    string Message,
    IReadOnlyList<FramePsdSlotDescriptor> Slots,
    FramePsdReadFailureKind FailureKind = FramePsdReadFailureKind.None,
    FramePsdEngineCapabilities? EngineCapabilities = null);

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
/// can export an unchanged supported stored composite to PNG. It never edits or saves layered PSDs.
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
            var slots = EnumerateLayers(document.Layers)
                .Select(layer => TryMapSlot(layer))
                .Where(slot => slot is not null)
                .Select(slot => slot!)
                .ToArray();

            var message = document.Warnings.Count == 0
                ? $"Read {document.Width}×{document.Height} {document.ColorMode} document; found {slots.Length} name-based slot candidate(s). Text values and editable masks are not inspected."
                : $"Read {document.Width}×{document.Height} {document.ColorMode} document; found {slots.Length} name-based slot candidate(s). {string.Join(" ", document.Warnings)}";
            return new FramePsdTemplateInspection(
                true,
                message,
                Array.AsReadOnly(slots),
                FramePsdReadFailureKind.None,
                Capabilities);
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
            return new FramePsdExportResult(false, "SlotAssignments must be provided; use an empty dictionary for an unchanged flattened preview copy.");
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

        if (document.CompositePreview is null)
        {
            var detail = document.Warnings.Count == 0 ? string.Empty : $" {string.Join(" ", document.Warnings)}";
            return new FramePsdExportResult(false, $"No supported stored composite preview is available; no output was written.{detail}");
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
            return new FramePsdExportResult(false, $"Flattened preview export failed: {exception.Message}");
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

        if (exception is FramePsdReaderFallbackException fallbackFailure)
        {
            var errorType = fallbackFailure.FallbackException.GetType().Name;
            return $"PsdSharp 1.2.0 and the configured psd-tools desktop fallback could not read the input ({errorType}). This reader failure does not establish that the source file is corrupt. No output was written.";
        }

        var cause = Unwrap(exception);
        var detail = $"{cause.GetType().Name}: {cause.Message}";
        return ClassifyReadFailure(exception) switch
        {
            FramePsdReadFailureKind.SourceNotFound => "The source PSD/PSB was not found; no output was written.",
            FramePsdReadFailureKind.AccessDenied => "Access to the source PSD/PSB was denied; no output was written.",
            FramePsdReadFailureKind.UnsupportedFeature => $"PsdSharp 1.2.0 reported unsupported PSD/PSB content ({detail}). This is a reader-compatibility limitation, not evidence that the source file is corrupt. No output was written.",
            FramePsdReadFailureKind.ReaderCouldNotInterpretInput => $"PsdSharp 1.2.0 could not interpret the input ({detail}). This does not establish that the source file is corrupt. No output was written.",
            _ => $"PsdSharp 1.2.0 failed while reading the source ({detail}). This reader error does not establish that the source file is corrupt. No output was written."
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

    private static FramePsdSlotDescriptor? TryMapSlot(FramePsdLayer layer)
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

        return type.HasValue
            ? new FramePsdSlotDescriptor(layer.Id, layer.Name, type.Value, layer.Name)
            : null;
    }

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
