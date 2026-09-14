using SharpCompress.Archives;

namespace BMachine.UI.Services.Explorer;

public sealed record SevenThemeEntry(string Path, long Size, bool IsLikelyResource);
public sealed record SevenThemeInspectionResult(string PackagePath, IReadOnlyList<SevenThemeEntry> Entries, IReadOnlyList<string> ResourceCandidates, string? Error)
{
    public bool IsValid => Error is null;
}

public sealed class SevenThemePackageService
{
    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".dll", ".mui", ".icl", ".ico", ".png", ".bmp", ".res", ".msstyles", ".theme" };

    public SevenThemeInspectionResult Inspect(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) return Invalid(packagePath, "Package path is empty.");
        if (!string.Equals(Path.GetExtension(packagePath), ".7z", StringComparison.OrdinalIgnoreCase)) return Invalid(packagePath, "Package must use .7z extension.");
        if (!File.Exists(packagePath)) return Invalid(packagePath, "Package file does not exist.");
        try
        {
            using var archive = ArchiveFactory.Open(packagePath);
            var entries = new List<SevenThemeEntry>();
            foreach (var entry in archive.Entries.Where(item => !item.IsDirectory))
            {
                var normalized = entry.Key.Replace('\\', '/');
                if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == "..")) return Invalid(packagePath, "Package contains an unsafe path.");
                entries.Add(new SevenThemeEntry(normalized, entry.Size, ResourceExtensions.Contains(Path.GetExtension(normalized))));
            }
            return new SevenThemeInspectionResult(packagePath, entries, entries.Where(item => item.IsLikelyResource).Select(item => item.Path).ToArray(), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        { return Invalid(packagePath, $"Could not inspect package: {ex.Message}"); }
    }

    private static SevenThemeInspectionResult Invalid(string? path, string error) => new(path ?? string.Empty, Array.Empty<SevenThemeEntry>(), Array.Empty<string>(), error);
}
