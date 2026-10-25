namespace FrameStudio.Engine;

/// <summary>
/// Sanitized structural hints for the user's Single (XL only) and Collage (XL + S + small background) workflows.
/// These hints are descriptive and never authorize layer placement, visibility changes, or PSD writing.
/// </summary>
public sealed record FramePsdWorkflowHints(
    int FotoGroupCount,
    int XlSubgroupCount,
    int SmallSubgroupCount,
    int XlSmartObjectCandidateCount,
    int SmallSmartObjectCandidateCount,
    int SmallBackgroundGroupCount)
{
    public bool HasSingleCandidate => FotoGroupCount == 1
        && XlSubgroupCount == 1
        && XlSmartObjectCandidateCount > 0;

    public bool HasCollageCandidates => HasSingleCandidate
        && SmallSubgroupCount == 1
        && SmallSmartObjectCandidateCount > 0
        && SmallBackgroundGroupCount == 1;

    public string ToSafeSummary()
    {
        if (FotoGroupCount == 0)
            return "Grup FOTO belum terdeteksi. Nama layer hanya petunjuk; placement dan perubahan visibility tetap terkunci.";
        if (FotoGroupCount != 1)
            return "Ditemukan beberapa grup FOTO; struktur Single/KOLASE ambigu dan tetap terkunci.";
        if (HasCollageCandidates)
            return "Petunjuk struktur KOLASE terdeteksi: kandidat Smart Object XL + S dan satu grup BG kecil. Penggantian foto/visibility belum aktif sampai mask serta render komposit mode diverifikasi.";
        if (HasSingleCandidate)
            return "Petunjuk struktur Single terdeteksi: kandidat Smart Object XL. Slot S dan perubahan grup tetap tidak disentuh; placement belum aktif sampai mask diverifikasi.";
        return "Grup FOTO terdeteksi, tetapi kandidat XL/S atau BG kecil belum lengkap. Placement dan perubahan visibility tetap terkunci.";
    }
}

/// <summary>Finds only structural hints from nested layer names; never treats names as validated apertures.</summary>
public static class FramePsdWorkflowGroupHeuristics
{
    public static FramePsdWorkflowHints Analyze(IReadOnlyList<FramePsdLayer> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var all = Flatten(roots).ToArray();
        var fotoGroups = all.Where(layer => layer.IsGroup && HasFotoToken(layer.Name)).ToArray();
        var backgroundGroups = all.Where(layer => layer.IsGroup && IsSmallBackgroundName(layer.Name)).ToArray();
        if (fotoGroups.Length != 1)
            return new FramePsdWorkflowHints(fotoGroups.Length, 0, 0, 0, 0, backgroundGroups.Length);

        var descendants = Flatten(fotoGroups[0].Children).ToArray();
        var xlSubgroups = SelectRoleCandidates(descendants, "xl");
        var smallSubgroups = SelectRoleCandidates(descendants, "s");
        return new FramePsdWorkflowHints(
            fotoGroups.Length,
            xlSubgroups.Length,
            smallSubgroups.Length,
            CountSmartObjects(xlSubgroups),
            CountSmartObjects(smallSubgroups),
            backgroundGroups.Length);
    }

    private static int CountSmartObjects(IEnumerable<FramePsdLayer> roleLayers)
    {
        var count = 0;
        foreach (var role in roleLayers)
        {
            count += Flatten(new[] { role }).Count(layer => IsSmartObject(layer.Kind));
        }
        return count;
    }

    private static FramePsdLayer[] SelectRoleCandidates(IReadOnlyList<FramePsdLayer> descendants, string role)
    {
        var groups = descendants.Where(layer => layer.IsGroup && HasToken(layer.Name, role)).ToArray();
        return groups.Length > 0
            ? groups
            : descendants.Where(layer => IsSmartObject(layer.Kind) && HasToken(layer.Name, role)).ToArray();
    }

    private static bool IsSmartObject(string? kind) => string.Equals(kind, "smartobject", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "smart object", StringComparison.OrdinalIgnoreCase);

    private static bool HasFotoToken(string? name) => Tokens(name).Any(token => token == "foto" || (token.StartsWith("foto", StringComparison.Ordinal) && token.Length > 4 && token[4..].All(char.IsAsciiDigit)));

    private static bool IsSmallBackgroundName(string? name)
    {
        var tokens = Tokens(name);
        return (tokens.Contains("bg") || tokens.Contains("background")) && tokens.Contains("kecil");
    }

    private static bool HasToken(string? name, string expected) => Tokens(name).Contains(expected);

    private static string[] Tokens(string? name) => (name ?? string.Empty)
        .Split(new[] { ' ', '\t', '\r', '\n', '-', '_', '[', ']', '(', ')', ':', '/', '\\', '.' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(token => token.Trim().ToLowerInvariant())
        .Where(token => token.Length > 0)
        .ToArray();

    private static IEnumerable<FramePsdLayer> Flatten(IEnumerable<FramePsdLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
                yield return child;
        }
    }
}
