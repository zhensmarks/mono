using System.IO;

namespace BMachine.UI.Services;

/// <summary>
/// Single source of truth for "copy" file/folder naming, Windows-Explorer style:
/// <c>name - Copy</c>, then <c>name - Copy (2)</c>, <c>name - Copy (3)</c>, ...
/// All duplicate-producing paths (paste, drag-drop, duplicate) MUST use this helper so
/// the three call sites can never drift apart again.
/// </summary>
public static class FileNameDeduplicator
{
    /// <summary>
    /// Returns a destination path inside <paramref name="destinationFolder"/> that does not
    /// collide with the filesystem — and not with any name already reserved by a previous
    /// call in the same batch (<paramref name="reservedNames"/>, for queued background copies
    /// whose file does not exist on disk yet).
    /// </summary>
    public static string BuildCopyPath(string destinationFolder, string sourcePath, ISet<string>? reservedNames = null)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(trimmed);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(fileName))
            return Path.Combine(destinationFolder, fileName ?? string.Empty);

        var first = Path.Combine(destinationFolder, fileName);
        if (!Collides(first, reservedNames)) return first;

        int i = 1;
        while (true)
        {
            var suffix = i == 1 ? " - Copy" : $" - Copy ({i})";
            var candidate = Path.Combine(destinationFolder, baseName + suffix + extension);
            if (!Collides(candidate, reservedNames)) return candidate;
            i++;
        }
    }

    private static bool Collides(string path, ISet<string>? reservedNames)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        return reservedNames != null && reservedNames.Contains(Path.GetFileName(path));
    }
}
