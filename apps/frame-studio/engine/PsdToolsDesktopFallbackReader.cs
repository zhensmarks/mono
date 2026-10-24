using System.Diagnostics;
using System.Text.Json;

namespace FrameStudio.Engine;

/// <summary>Configuration for the optional, local-only psd-tools desktop reader.</summary>
public sealed record PsdToolsDesktopFallbackOptions
{
    public PsdToolsDesktopFallbackOptions(
        string pythonExecutablePath,
        long maxInputFileBytes = 512L * 1024 * 1024,
        long maxPreviewBytes = 256L * 1024 * 1024,
        long maxAllocationBytes = 1024L * 1024 * 1024,
        long processMemoryLimitBytes = 3L * 1024 * 1024 * 1024,
        TimeSpan? processTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutablePath);
        if (maxInputFileBytes <= 0 || maxInputFileBytes > 4L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputFileBytes));
        }

        if (maxPreviewBytes < 4 || maxPreviewBytes > 1024L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPreviewBytes));
        }

        if (maxAllocationBytes < 16L * 1024 * 1024 || maxAllocationBytes > 2L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllocationBytes));
        }

        if (processMemoryLimitBytes < maxAllocationBytes || processMemoryLimitBytes > 8L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(processMemoryLimitBytes));
        }

        var timeout = processTimeout ?? TimeSpan.FromSeconds(120);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
        }

        PythonExecutablePath = Path.IsPathRooted(pythonExecutablePath)
            ? Path.GetFullPath(pythonExecutablePath)
            : pythonExecutablePath;
        MaxInputFileBytes = maxInputFileBytes;
        MaxPreviewBytes = maxPreviewBytes;
        MaxAllocationBytes = maxAllocationBytes;
        ProcessMemoryLimitBytes = processMemoryLimitBytes;
        ProcessTimeout = timeout;
    }

    public string PythonExecutablePath { get; }
    public long MaxInputFileBytes { get; }
    public long MaxPreviewBytes { get; }
    public long MaxAllocationBytes { get; }
    public long ProcessMemoryLimitBytes { get; }
    public TimeSpan ProcessTimeout { get; }
}

/// <summary>
/// Optional local desktop reader. It invokes a configured Python runtime and psd-tools 1.26.0;
/// this is a read-only fallback, not a PSD writer or Photoshop renderer.
/// </summary>
public sealed class PsdToolsDesktopFallbackReader
{
    private const string EmbeddedBridgeResource = "FrameStudio.Engine.psd_tools_reader.py";
    private const long MaxManifestBytes = 16L * 1024 * 1024;
    private const int MaxLayers = 20_000;
    private const int MaxLayerDepth = 32;
    private readonly PsdToolsDesktopFallbackOptions _options;
    private int _runtimeVerified;
    private int _processMemoryLimitState = -1;
    private string _verifiedPythonVersion = string.Empty;

    public PsdToolsDesktopFallbackReader(PsdToolsDesktopFallbackOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public long MaxInputFileBytes => _options.MaxInputFileBytes;

    public FramePsdOptionalBackendCapabilities Capabilities => new(
        Name: "psd-tools desktop sidecar",
        Version: Volatile.Read(ref _runtimeVerified) == 1
            ? $"1.26.0 (verified; Python {Volatile.Read(ref _verifiedPythonVersion)})"
            : "1.26.0 (required; runtime not probed until used)",
        License: "MIT",
        RuntimeRequirement: "Python 3.11+ and psd-tools==1.26.0 with its declared dependencies installed locally.",
        PlatformScope: "Desktop only; this proof of concept was exercised on Linux x86_64. Other desktop platforms are unverified; no mobile support is claimed.",
        IsConfigured: true,
        RuntimeVerified: Volatile.Read(ref _runtimeVerified) == 1,
        CanAttemptPsdRead: true,
        CanAttemptPsbRead: true,
        CanInspectLayerTreeBestEffort: true,
        CanExportSupportedStoredCompositePng: true,
        CanWriteLayeredPsd: false,
        Limitations: "Reads the stored merged composite, not a Photoshop-accurate recomposition. Text values are omitted. Layer metadata is best-effort. No pixel, text, mask, effect, or layered-PSD editing is supported.")
    {
        ProcessMemoryLimitApplied = Volatile.Read(ref _processMemoryLimitState) < 0
            ? null
            : Volatile.Read(ref _processMemoryLimitState) == 1
    };

    internal FramePsdDocument Read(string sourcePath, string sourceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(fullPath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("The source PSD/PSB was not found.");
        }

        if (sourceInfo.Length > _options.MaxInputFileBytes)
        {
            throw new NotSupportedException("The source exceeds the configured psd-tools fallback input-size limit.");
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"frame-studio-psd-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var bridgePath = ExtractBridge(temporaryDirectory);
            var manifestPath = Path.Combine(temporaryDirectory, "manifest.json");
            var rgbaPath = Path.Combine(temporaryDirectory, "composite.rgba");
            RunBridge(bridgePath, fullPath, manifestPath, rgbaPath, cancellationToken);
            var manifest = ReadManifest(manifestPath);
            if (!manifest.Success)
            {
                throw new InvalidDataException($"The configured psd-tools fallback could not read this file ({SafeErrorType(manifest.ErrorType)}).");
            }

            if (!string.Equals(manifest.PsdToolsVersion, "1.26.0", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(manifest.PythonVersion))
            {
                throw new NotSupportedException("The configured sidecar runtime does not match psd-tools 1.26.0.");
            }

            Volatile.Write(ref _processMemoryLimitState, manifest.ProcessMemoryLimitApplied ? 1 : 0);
            if (!manifest.ProcessMemoryLimitApplied)
            {
                throw new NotSupportedException("The optional psd-tools reader could not enforce its process memory limit.");
            }

            var document = MapDocument(manifest, rgbaPath, sourceName, fullPath);
            Volatile.Write(ref _verifiedPythonVersion, manifest.PythonVersion);
            Volatile.Write(ref _runtimeVerified, 1);
            return document;
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private string ExtractBridge(string directory)
    {
        var path = Path.Combine(directory, "psd_tools_reader.py");
        using var input = typeof(PsdToolsDesktopFallbackReader).Assembly.GetManifestResourceStream(EmbeddedBridgeResource)
            ?? throw new InvalidOperationException("The embedded psd-tools bridge is missing from the engine assembly.");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        return path;
    }

    private void RunBridge(
        string bridgePath,
        string sourcePath,
        string manifestPath,
        string rgbaPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_options.PythonExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(bridgePath)!
        };
        startInfo.ArgumentList.Add(bridgePath);
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(manifestPath);
        startInfo.ArgumentList.Add(rgbaPath);
        startInfo.ArgumentList.Add(_options.MaxAllocationBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(_options.MaxPreviewBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(_options.ProcessMemoryLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(_options.MaxInputFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The configured psd-tools reader process did not start.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new NotSupportedException("The configured Python runtime is unavailable for the optional PSD reader.", exception);
        }

        var stdoutDrain = DrainAndDiscardAsync(process.StandardOutput);
        var stderrDrain = DrainAndDiscardAsync(process.StandardError);
        using var cancellationRegistration = cancellationToken.Register(static state => TryKill((Process)state!), process);
        var timeoutMilliseconds = checked((int)Math.Ceiling(_options.ProcessTimeout.TotalMilliseconds));
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The configured psd-tools reader exceeded its process time limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Task.WhenAll(stdoutDrain, stderrDrain).GetAwaiter().GetResult();
        if (process.ExitCode != 0 && !File.Exists(manifestPath))
        {
            throw new InvalidDataException("The configured psd-tools reader exited without a result.");
        }
    }

    private static async Task DrainAndDiscardAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) > 0)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static BridgeDocument ReadManifest(string manifestPath)
    {
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length <= 0 || info.Length > MaxManifestBytes)
        {
            throw new InvalidDataException("The optional PSD reader returned an invalid or oversized result manifest.");
        }

        using var input = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<BridgeDocument>(input, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = MaxLayerDepth + 8
        }) ?? throw new InvalidDataException("The optional PSD reader returned an empty result manifest.");
    }

    private FramePsdDocument MapDocument(BridgeDocument manifest, string rgbaPath, string sourceName, string sourcePath)
    {
        if (manifest.Width <= 0 || manifest.Height <= 0 || manifest.Width > 30_000 || manifest.Height > 30_000)
        {
            throw new InvalidDataException("The optional PSD reader returned out-of-range document dimensions.");
        }

        if (manifest.Layers is null || manifest.Warnings is null)
        {
            throw new InvalidDataException("The optional PSD reader omitted required layer metadata.");
        }

        var seen = new LayerLimits();
        var layers = manifest.Layers.Select(layer => MapLayer(layer, seen, depth: 1)).ToArray();
        var rgbaLength = checked((long)manifest.Width * manifest.Height * 4);
        FrameCompositePreview? preview = null;
        if (manifest.PreviewBytes > 0)
        {
            if (rgbaLength > _options.MaxPreviewBytes || manifest.PreviewBytes != rgbaLength || rgbaLength > int.MaxValue)
            {
                throw new InvalidDataException("The optional PSD reader returned a composite outside the configured memory limit.");
            }

            var previewInfo = new FileInfo(rgbaPath);
            if (!previewInfo.Exists || previewInfo.Length != rgbaLength)
            {
                throw new InvalidDataException("The optional PSD reader returned an incomplete composite buffer.");
            }

            preview = new FrameCompositePreview(manifest.Width, manifest.Height, File.ReadAllBytes(rgbaPath));
        }
        else if (File.Exists(rgbaPath))
        {
            throw new InvalidDataException("The optional PSD reader returned unexpected composite data.");
        }

        var colorMode = manifest.ColorMode switch
        {
            "RGB" => "Rgb",
            "CMYK" => "Cmyk",
            _ => manifest.ColorMode
        };
        var slots = Flatten(layers)
            .Where(layer => layer.SlotHint.HasValue)
            .Select(layer => new FramePhotoSlotCandidate(layer.Id, layer.Name, layer.SlotHint!.Value, layer.Bounds))
            .ToArray();
        var warnings = manifest.Warnings.ToList();
        warnings.Add("Read-only preview/tree supplied by the optional psd-tools desktop sidecar; layered export and editing remain disabled.");
        if (!manifest.ProcessMemoryLimitApplied)
        {
            warnings.Add("This platform did not apply an operating-system process memory limit to the Python reader; parser and preview allocation limits still apply.");
        }

        return new FramePsdDocument(
            Path.GetFileName(sourceName),
            sourcePath,
            manifest.Width,
            manifest.Height,
            checked((ushort)manifest.ChannelCount),
            checked((byte)manifest.BitDepth),
            colorMode,
            Array.AsReadOnly(layers),
            Array.AsReadOnly(slots),
            preview,
            Array.AsReadOnly(warnings.ToArray()),
            readerBackend: "psd-tools desktop sidecar");
    }

    private static FramePsdLayer MapLayer(BridgeLayer layer, LayerLimits limits, int depth)
    {
        if (depth > MaxLayerDepth || ++limits.Count > MaxLayers)
        {
            throw new InvalidDataException("The optional PSD reader exceeded the configured layer-tree limits.");
        }

        if (layer.Children is null || layer.Name is null || layer.Id is null || layer.Kind is null)
        {
            throw new InvalidDataException("The optional PSD reader returned incomplete layer metadata.");
        }

        if (layer.Name.Length > 512 || layer.Id.Length > 1024 || layer.Width < 0 || layer.Height < 0)
        {
            throw new InvalidDataException("The optional PSD reader returned out-of-range layer metadata.");
        }

        var children = layer.Children.Select(child => MapLayer(child, limits, depth + 1)).ToArray();
        var opacity = checked((byte)Math.Clamp(layer.Opacity, 0, 255));
        var bounds = new FrameBounds(layer.X, layer.Y, layer.Width, layer.Height);
        return new FramePsdLayer(
            layer.Id,
            layer.Name,
            layer.Visible,
            opacity,
            bounds,
            layer.HasPixelData,
            SlotNameClassifier.Classify(layer.Name),
            Array.AsReadOnly(children))
        {
            Kind = layer.Kind,
            IsGroup = layer.IsGroup,
            HasMask = layer.HasMask,
            HasVectorMask = layer.HasVectorMask,
            HasEffects = layer.HasEffects
        };
    }

    private static IEnumerable<FramePsdLayer> Flatten(IEnumerable<FramePsdLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }

    private static string SafeErrorType(string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType) || errorType.Length > 80 || errorType.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            return "UnknownError";
        }

        return errorType;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class LayerLimits
    {
        public int Count { get; set; }
    }

    private sealed class BridgeDocument
    {
        public bool Success { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ChannelCount { get; set; }
        public int BitDepth { get; set; }
        public string ColorMode { get; set; } = string.Empty;
        public string? PsdToolsVersion { get; set; }
        public string? PythonVersion { get; set; }
        public long PreviewBytes { get; set; }
        public bool ProcessMemoryLimitApplied { get; set; }
        public List<BridgeLayer>? Layers { get; set; }
        public List<string>? Warnings { get; set; }
        public string? ErrorType { get; set; }
    }

    private sealed class BridgeLayer
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public bool IsGroup { get; set; }
        public bool Visible { get; set; }
        public int Opacity { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool HasPixelData { get; set; }
        public bool HasMask { get; set; }
        public bool HasVectorMask { get; set; }
        public bool HasEffects { get; set; }
        public List<BridgeLayer>? Children { get; set; }
    }
}


internal sealed class FramePsdReaderFallbackException : Exception
{
    public FramePsdReaderFallbackException(Exception primaryException, Exception fallbackException)
        : base("The primary PSD reader and configured fallback both failed.", fallbackException)
    {
        PrimaryException = primaryException;
        FallbackException = fallbackException;
    }

    public Exception PrimaryException { get; }
    public Exception FallbackException { get; }
}
