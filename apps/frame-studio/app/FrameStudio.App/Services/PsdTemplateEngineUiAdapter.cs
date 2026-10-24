using System.Runtime.InteropServices;
using FrameStudio.Engine;

namespace FrameStudio.App.Services;

/// <summary>Maps the UI contract to the read-only PSD engine; it never edits layered PSD data.</summary>
public sealed class PsdTemplateEngineUiAdapter : IPsdTemplateAdapter
{
    private readonly PsdTemplateEngineAdapter _engine;
    private readonly string _fallbackStatus;

    public PsdTemplateEngineUiAdapter(PsdTemplateEngineAdapter? engine = null)
    {
        if (engine is not null)
        {
            _engine = engine;
            _fallbackStatus = "Reader configuration was supplied by the host.";
        }
        else
        {
            (_engine, _fallbackStatus) = CreateDefaultEngine();
        }
    }

    public bool IsAvailable => _engine.IsAvailable;
    public FramePsdEngineCapabilities Capabilities => _engine.Capabilities;

    public string CapabilityNotice
    {
        get
        {
            var fallback = Capabilities.OptionalFallback;
            if (!IsTestedPlatform())
                return $"{RuntimePlatformLabel()} · optional psd-tools reader gated off · layered PSD disabled";
            if (fallback is null)
                return $"{RuntimePlatformLabel()} · psd-tools reader unavailable · layered PSD disabled";
            var runtimeStatus = fallback.RuntimeVerified ? "runtime verified" : "runtime configured; verification pending";
            return $"{RuntimePlatformLabel()} · psd-tools 1.26.0 · {runtimeStatus} · read-only · layered PSD disabled";
        }
    }

    public string CapabilityDetails
    {
        get
        {
            var fallback = Capabilities.OptionalFallback;
            var requirement = "Optional reader is tested only on Linux x86_64 and requires Python 3.11+ plus psd-tools==1.26.0 installed locally.";
            var resourceLimits = "Sidecar bounds: 512 MiB input, 1 GiB per-allocation, 256 MiB RGBA preview, 20,000 layer nodes, depth 32, 3 GiB child-process address-space cap, and 120-second timeout. PsdSharp runs in-process and is not separately OS-memory-sandboxed.";
            var memoryCapStatus = fallback?.ProcessMemoryLimitApplied switch
            {
                true => "The sidecar OS process-memory cap was applied on the last successful read.",
                false => "The sidecar OS process-memory cap was not applied; fallback reads are refused.",
                _ => "The sidecar OS process-memory cap is verified during the first PSD read."
            };
            var details = fallback is null
                ? _fallbackStatus
                : $"{fallback.Name}: {fallback.Version}. {fallback.RuntimeRequirement} {fallback.PlatformScope} {fallback.Limitations}";
            return $"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). {requirement} {resourceLimits} {memoryCapStatus} {details} Layered PSD writing and photo/text/logo editing are disabled in fallback preview mode; PNG output remains separate from the master.";
        }
    }

    public async Task<PsdTemplateInspection> InspectAsync(
        string masterPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _engine.InspectAsync(masterPath, cancellationToken);
        var counts = new Dictionary<PsdSlotType, int>();
        var slots = result.Slots.Select(slot =>
        {
            var type = slot.Type switch
            {
                FramePsdSlotType.Photo => PsdSlotType.Photo,
                FramePsdSlotType.Text => PsdSlotType.Text,
                FramePsdSlotType.Logo => PsdSlotType.Logo,
                _ => throw new ArgumentOutOfRangeException(nameof(slot.Type))
            };
            counts.TryGetValue(type, out var count);
            count++;
            counts[type] = count;
            var label = $"{type} candidate {count}";
            return new PsdSlotDescriptor(
                slot.Id,
                label,
                type,
                label,
                CurrentValue: null,
                slot.Bounds is { } bounds
                    ? new PsdSlotBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                    : null);
        }).ToArray();

        return new PsdTemplateInspection(
            result.Success,
            result.Message,
            Array.AsReadOnly(slots),
            result.FlattenedPreviewPng,
            result.DocumentWidth,
            result.DocumentHeight,
            result.LayerTree,
            result.EngineCapabilities,
            result.ActiveReaderName,
            result.FailureKind);
    }

    private static (PsdTemplateEngineAdapter Engine, string FallbackStatus) CreateDefaultEngine()
    {
        if (!IsTestedPlatform())
        {
            return (new PsdTemplateEngineAdapter(), "psd-tools fallback is restricted to Linux x86_64; no fallback runtime is configured on this platform.");
        }

        var configuredPython = Environment.GetEnvironmentVariable("FRAME_STUDIO_PSD_TOOLS_PYTHON");
        var pythonExecutable = ResolveExecutable(string.IsNullOrWhiteSpace(configuredPython) ? "python3" : configuredPython);
        if (pythonExecutable is null)
        {
            return (new PsdTemplateEngineAdapter(), "Python 3.11+ was not found. Install it and psd-tools==1.26.0 locally; set FRAME_STUDIO_PSD_TOOLS_PYTHON to select a Python executable.");
        }

        var fallbackReader = new PsdToolsDesktopFallbackReader(new PsdToolsDesktopFallbackOptions(pythonExecutable));
        var reader = new PsdDocumentReader(fallbackReader: fallbackReader);
        return (new PsdTemplateEngineAdapter(reader), "Python executable found; Python version and psd-tools==1.26.0 are verified when a local PSD is inspected.");
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (Path.IsPathRooted(candidate))
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }

    private static bool IsTestedPlatform() => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    private static string RuntimePlatformLabel() => $"{(OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription)} {RuntimeInformation.ProcessArchitecture}";
}
