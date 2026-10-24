namespace FrameStudio.Engine;

public enum FramePsdSlotType
{
    Photo,
    Text,
    Logo
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
    IReadOnlyList<FramePsdSlotDescriptor> Slots);

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
    string? OutputPath = null);

/// <summary>
/// Async bridge for the Avalonia template workflow. The current implementation inspects layer-name
/// hints and can export an unchanged flattened PNG copy only; it never edits or saves layered PSDs.
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

    public bool IsAvailable => true;

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
            var document = _reader.Read(masterPath);
            cancellationToken.ThrowIfCancellationRequested();
            var slots = EnumerateLayers(document.Layers)
                .Select(layer => TryMapSlot(layer))
                .Where(slot => slot is not null)
                .Select(slot => slot!)
                .ToArray();

            var message = document.Warnings.Count == 0
                ? $"Read {document.Width}×{document.Height} {document.ColorMode} document; found {slots.Length} name-based slot candidate(s). Text values and editable masks are not inspected."
                : $"Read {document.Width}×{document.Height} {document.ColorMode} document; found {slots.Length} name-based slot candidate(s). {string.Join(" ", document.Warnings)}";
            return new FramePsdTemplateInspection(true, message, Array.AsReadOnly(slots));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new FramePsdTemplateInspection(false, $"PSD inspection failed: {exception.Message}", Array.Empty<FramePsdSlotDescriptor>());
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
            return new FramePsdExportResult(false, "This POC cannot apply text/photo/logo assignments or save a layered PSD. No output was written; clear assignments to export the unchanged flattened PNG preview.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var document = _reader.Read(request.MasterPath);
            if (document.CompositePreview is null)
            {
                var detail = document.Warnings.Count == 0 ? string.Empty : $" {string.Join(" ", document.Warnings)}";
                return new FramePsdExportResult(false, $"No supported composite preview is available; no output was written.{detail}");
            }

            cancellationToken.ThrowIfCancellationRequested();
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
            return new FramePsdExportResult(false, $"Flattened copy export failed: {exception.Message}");
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
