using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public class PhotoMatcherService
{
    private static readonly Regex LeadingNumberRegex = new(@"^\d+\s*[\.\-\)]\s*", RegexOptions.Compiled);
    private static readonly Regex TrailingDupRegex = new(@"\(\s*\d+\s*\)\s*$", RegexOptions.Compiled);
    private static readonly Regex CleanNonAlphaRegex = new(@"[^a-z0-9 ]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return string.Empty;
        var s = Path.GetFileNameWithoutExtension(str).ToLowerInvariant();
        s = CleanNonAlphaRegex.Replace(s, " ");
        s = MultiSpaceRegex.Replace(s, " ").Trim();
        return s;
    }

    private static readonly Regex NamaPrefixRegex = new(@"^\s*nama\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DegreeSuffixRegex = new(@"\s+(?:(?:[smd]\.[a-z.]+)|(?:gr|dr|dra|drs|ir)\.?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CleanFileNameForMatch(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var s = Path.GetFileNameWithoutExtension(fileName);

        // Buang label "Nama :" jika ada di dalam nilai data
        s = NamaPrefixRegex.Replace(s, "");

        // Buang gelar setelah koma (misal: "Deni Setiawan, S.Pd." -> "Deni Setiawan")
        int commaAt = s.IndexOf(',');
        if (commaAt >= 0) s = s.Substring(0, commaAt);

        // Buang gelar sederhana tanpa koma (misal: "Deni Setiawan S.Pd.")
        s = DegreeSuffixRegex.Replace(s, "");

        s = Normalize(s);
        s = LeadingNumberRegex.Replace(s, "");
        s = TrailingDupRegex.Replace(s, "");
        return Normalize(s);
    }

    public static List<string> Tokenize(string s)
    {
        var parts = Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();
        foreach (var p in parts)
        {
            if (p.Length < 2) continue;
            if (int.TryParse(p, out _)) continue;
            tokens.Add(p);
        }
        return tokens;
    }

    public static int OverlapScore(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;

        var setA = new HashSet<string>(ta, StringComparer.OrdinalIgnoreCase);
        int score = 0;
        foreach (var t in tb)
        {
            if (setA.Contains(t)) score++;
        }
        return score;
    }

    public static int BestMatchScore(string nameText, string fileName)
    {
        var fileNorm = CleanFileNameForMatch(fileName);
        var want = Normalize(nameText);
        if (string.IsNullOrEmpty(want) || want.Length < 3) return 0;

        int score = OverlapScore(want, fileNorm);
        if (fileNorm == want)
        {
            score += 100;
        }
        else if (fileNorm.Contains(want, StringComparison.OrdinalIgnoreCase) || want.Contains(fileNorm, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        return score;
    }

    public PhotoMatchResult FindBestMatch(string studentName, List<string> photoFiles, int threshold = 12, bool forcePick = false)
    {
        string? bestFile = null;
        int bestScore = 0;

        foreach (var file in photoFiles)
        {
            var fileName = Path.GetFileName(file);
            int score = BestMatchScore(studentName, fileName);
            if (score > bestScore)
            {
                bestScore = score;
                bestFile = file;
            }
        }

        bool passed = forcePick ? (bestScore > 0 && bestFile != null) : (bestScore >= threshold && bestFile != null);

        return new PhotoMatchResult
        {
            StudentName = studentName,
            MatchedFilePath = passed ? bestFile : null,
            Score = bestScore,
            IsPassed = passed,
            BestCandidateName = bestFile != null ? Path.GetFileName(bestFile) : string.Empty,
            BestCandidatePath = bestFile ?? string.Empty,
            IsForced = forcePick && passed
        };
    }

    public List<string> CollectPhotosRecursive(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return new List<string>();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".psd" };
        try
        {
            return Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Logika pencocokan foto berdasarkan nama file Template PSD (ala BMachine replace.jsx)
    /// Mendukung format .png, .psd, .jpg dengan prioritas: PNG > PSD > JPG
    /// Memiliki toleransi terhadap spasi, suffix " (1)", penomoran angka, dan typo kecil
    /// </summary>
    public string? MatchPhotoToPsd(string psdFileName, List<string> photoFiles)
    {
        if (string.IsNullOrWhiteSpace(psdFileName) || photoFiles == null || photoFiles.Count == 0) return null;

        var psdBase = Path.GetFileNameWithoutExtension(psdFileName).Trim();
        var psdNorm = Normalize(psdBase);
        var psdClean = CleanFileNameForMatch(psdBase);

        // Ekstrak angka murni dari PSD jika ada (misal "01", "1")
        var psdNumMatch = Regex.Match(psdBase, @"\b(\d+)\b");
        var psdNumber = psdNumMatch.Success ? int.Parse(psdNumMatch.Groups[1].Value).ToString() : string.Empty;

        var candidates = new List<(string Path, int Priority, int Score)>();

        foreach (var file in photoFiles)
        {
            var fName = Path.GetFileName(file);
            var fExt = Path.GetExtension(file).ToLowerInvariant();
            var fBase = Path.GetFileNameWithoutExtension(fName).Trim();
            var fNorm = Normalize(fBase);
            var fClean = CleanFileNameForMatch(fBase);

            // Format priority (BMachine pattern: PNG > PSD > JPG)
            int formatPriority = fExt switch
            {
                ".png" => 3,
                ".psd" => 2,
                _ => 1
            };

            int score = 0;

            // 1. Exact match tanpa ekstensi (case-insensitive)
            if (string.Equals(fBase, psdBase, StringComparison.OrdinalIgnoreCase))
            {
                score = 1000;
            }
            // 2. Exact match setelah stripping spasi dan duplicate suffix "(1)"
            else if (string.Equals(fClean, psdClean, StringComparison.OrdinalIgnoreCase))
            {
                score = 800;
            }
            // 3. Substring match (salah satu mengandung yang lain)
            else if (!string.IsNullOrEmpty(psdClean) && (fClean.Contains(psdClean, StringComparison.OrdinalIgnoreCase) || psdClean.Contains(fClean, StringComparison.OrdinalIgnoreCase)))
            {
                score = 600;
            }
            // 4. Match angka murni jika template hanya penomoran (misal "1" cocok dengan "Foto (1).png" atau "1.jpg")
            else if (!string.IsNullOrEmpty(psdNumber))
            {
                var fNumMatch = Regex.Match(fBase, @"\b(\d+)\b");
                if (fNumMatch.Success && int.Parse(fNumMatch.Groups[1].Value).ToString() == psdNumber)
                {
                    score = 400;
                }
            }

            // 5. Toleransi Typo (Levenshtein distance)
            if (score == 0 && psdClean.Length >= 4 && fClean.Length >= 4)
            {
                int dist = ComputeLevenshteinDistance(psdClean, fClean);
                int maxLen = Math.Max(psdClean.Length, fClean.Length);
                double similarity = 1.0 - ((double)dist / maxLen);
                if (similarity >= 0.75) // toleransi typo 1-2 huruf
                {
                    score = (int)(similarity * 500);
                }
            }

            if (score > 0)
            {
                candidates.Add((file, formatPriority, score));
            }
        }

        if (candidates.Count == 0) return null;

        // Urutkan berdasarkan Skor kemiripan tertinggi, lalu Prioritas Format (.png > .psd > .jpg)
        var best = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Priority)
            .First();

        return best.Path;
    }

    /// <summary>
    /// Logika pencocokan baris data siswa ke file template PSD (Mode B)
    /// </summary>
    public int MatchDataRowToPsd(string studentName, string psdFileName)
    {
        if (string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(psdFileName)) return 0;

        var psdBase = Path.GetFileNameWithoutExtension(psdFileName);
        var sClean = CleanFileNameForMatch(studentName);
        var pClean = CleanFileNameForMatch(psdBase);

        if (string.Equals(sClean, pClean, StringComparison.OrdinalIgnoreCase)) return 1000;
        if (pClean.Contains(sClean, StringComparison.OrdinalIgnoreCase) || sClean.Contains(pClean, StringComparison.OrdinalIgnoreCase)) return 600;

        int overlap = OverlapScore(sClean, pClean);
        if (overlap >= 2) return 400 + overlap * 50;

        // Typo tolerance
        if (sClean.Length >= 4 && pClean.Length >= 4)
        {
            int dist = ComputeLevenshteinDistance(sClean, pClean);
            int maxLen = Math.Max(sClean.Length, pClean.Length);
            double sim = 1.0 - ((double)dist / maxLen);
            if (sim >= 0.75) return (int)(sim * 350);
        }

        return 0;
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
