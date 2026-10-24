namespace FrameStudio.App.Services;

/// <summary>Writes complete PNG byte arrays to unique filenames using a same-folder atomic move.</summary>
public sealed class PngOutputWriter
{
    public string WriteNewPng(string outputFolder, string stem, byte[] pngBytes, string? protectedPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        ArgumentNullException.ThrowIfNull(pngBytes);

        var folder = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(folder);
        var safeStem = Sanitize(stem);
        if (string.IsNullOrWhiteSpace(safeStem))
            safeStem = "frame-studio";

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var protectedFullPath = string.IsNullOrWhiteSpace(protectedPath) ? null : Path.GetFullPath(protectedPath);
        for (var suffix = 1; suffix <= 10_000; suffix++)
        {
            var fileName = suffix == 1 ? $"{safeStem}.png" : $"{safeStem}-{suffix}.png";
            var outputPath = Path.Combine(folder, fileName);
            if (protectedFullPath is not null && string.Equals(outputPath, protectedFullPath, comparison))
                continue;

            var temporaryPath = Path.Combine(folder, $".frame-studio-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(pngBytes);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporaryPath, outputPath);
                    return outputPath;
                }
                catch (IOException) when (File.Exists(outputPath))
                {
                    // The destination was claimed by another writer; keep the existing file and try a suffix.
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        throw new IOException("Tidak ditemukan nama file output PNG yang belum digunakan.");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return safe.TrimEnd('.', ' ');
    }
}
