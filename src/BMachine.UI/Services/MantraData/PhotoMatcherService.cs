using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using BMachine.UI.Models.MantraData;

namespace BMachine.UI.Services.MantraData;

public class PhotoMatcherService
{
    private static readonly Regex FileExtRegex = new(@"\.(jpe?g|png)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingNumRegex = new(@"^\s*\d+\s*[.\-)]+\s*", RegexOptions.Compiled);
    private static readonly Regex TrailingDupRegex = new(@"\(\s*\d+\s*\)\s*$", RegexOptions.Compiled);
    private static readonly Regex NonAlnumRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Ekstrak nama bersih dari nilai cell — ambil baris pertama saja (abaikan NISN/kota/dll
    /// yang mungkin ada di baris berikutnya dalam satu cell multiline).
    /// </summary>
    public static string ExtractNameFromCell(string cellValue)
    {
        if (string.IsNullOrWhiteSpace(cellValue)) return string.Empty;
        // Ambil baris pertama yang tidak kosong
        foreach (var line in cellValue.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                return trimmed;
        }
        return cellValue.Trim();
    }

    public static string NormalizePersonName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Normalize(NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        var result = sb.ToString().Normalize(NormalizationForm.FormC);
        result = result.ToLower(CultureInfo.GetCultureInfo("id-ID"));
        result = FileExtRegex.Replace(result, "");
        result = Regex.Replace(result, @"^\s*\(\s*\d+\s*\)\s*", "");
        result = LeadingNumRegex.Replace(result, "");
        result = TrailingDupRegex.Replace(result, "");
        result = NonAlnumRegex.Replace(result, " ");
        return result.Trim();
    }

    public static string CleanFileNameForMatch(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        var s = Path.GetFileNameWithoutExtension(fileName);

        s = Regex.Replace(s, @"^\s*nama\s*:\s*", "", RegexOptions.IgnoreCase);

        int commaAt = s.IndexOf(',');
        if (commaAt >= 0) s = s.Substring(0, commaAt);

        s = Regex.Replace(s, @"\s+(?:(?:[smd]\.[a-z.]+)|(?:gr|dr|dra|drs|ir)\.?)\s*$", "", RegexOptions.IgnoreCase);

        return NormalizePersonName(s);
    }

    public static int Score(string name, string photoStem)
    {
        var wanted = NormalizePersonName(name);
        var candidate = NormalizePersonName(photoStem);
        if (string.IsNullOrEmpty(wanted) || string.IsNullOrEmpty(candidate)) return 0;
        if (wanted == candidate) return 100;

        var wantedTokens = new HashSet<string>(wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var candidateTokens = new HashSet<string>(candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        int overlap = wantedTokens.Intersect(candidateTokens).Count();
        int union = wantedTokens.Union(candidateTokens).Count();
        if (union == 0) union = 1;

        double tokenScore = 45.0 * overlap / union;
        double sequenceScore = 35.0 * SequenceRatio(wanted, candidate);
        double containsBonus = (wanted.Contains(candidate, StringComparison.Ordinal) || candidate.Contains(wanted, StringComparison.Ordinal)) ? 20.0 : 0.0;

        return Math.Min(99, (int)Math.Round(tokenScore + sequenceScore + containsBonus));
    }

    private static double SequenceRatio(string a, string b)
    {
        int la = a.Length, lb = b.Length;
        if (la == 0 && lb == 0) return 1.0;
        if (la == 0 || lb == 0) return 0.0;

        var d = new int[la + 1, lb + 1];
        for (int i = 0; i <= la; i++) d[i, 0] = i;
        for (int j = 0; j <= lb; j++) d[0, j] = j;

        for (int i = 1; i <= la; i++)
            for (int j = 1; j <= lb; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }

        return 1.0 - (double)d[la, lb] / Math.Max(la, lb);
    }

    public static List<string> CollectPhotos(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return new List<string>();
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };
        try
        {
            return Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<string> CollectPhotosRecursive(string rootFolder) => CollectPhotos(rootFolder);

    private static readonly Regex NumberRegex = new(@"\b(\d+)\b", RegexOptions.Compiled);

    public string? MatchPhotoToPsd(string psdFileName, List<string> photoFiles)
    {
        if (string.IsNullOrWhiteSpace(psdFileName) || photoFiles == null || photoFiles.Count == 0) return null;

        var psdBase = Path.GetFileNameWithoutExtension(psdFileName).Trim();
        var psdClean = CleanFileNameForMatch(psdBase);
        var psdNumMatch = NumberRegex.Match(psdBase);
        var psdNumber = psdNumMatch.Success ? int.Parse(psdNumMatch.Groups[1].Value).ToString() : string.Empty;

        var candidates = new List<(string Path, int Score)>();

        foreach (var file in photoFiles)
        {
            var fBase = Path.GetFileNameWithoutExtension(file);
            var fClean = CleanFileNameForMatch(fBase);

            int score = 0;
            if (string.Equals(fBase, psdBase, StringComparison.OrdinalIgnoreCase))
                score = 1000;
            else if (string.Equals(fClean, psdClean, StringComparison.OrdinalIgnoreCase))
                score = 800;
            else if (!string.IsNullOrEmpty(psdClean) &&
                     (fClean.Contains(psdClean, StringComparison.OrdinalIgnoreCase) || psdClean.Contains(fClean, StringComparison.OrdinalIgnoreCase)))
                score = 600;
            else if (!string.IsNullOrEmpty(psdNumber))
            {
                var fNumMatch = NumberRegex.Match(fBase);
                if (fNumMatch.Success && int.Parse(fNumMatch.Groups[1].Value).ToString() == psdNumber)
                    score = 400;
            }

            if (score > 0)
                candidates.Add((file, score));
        }

        if (candidates.Count == 0) return null;

        return candidates
            .OrderByDescending(c => c.Score)
            .First().Path;
    }

    public int MatchDataRowToPsd(string studentName, string psdFileName)
    {
        if (string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(psdFileName)) return 0;

        // Ekstrak nama bersih dari cell multiline (ambil baris pertama saja)
        var cleanedName = ExtractNameFromCell(studentName);

        var psdBase = Path.GetFileNameWithoutExtension(psdFileName);
        var sClean = CleanFileNameForMatch(cleanedName);
        var pClean = CleanFileNameForMatch(psdBase);

        if (string.IsNullOrEmpty(sClean) || string.IsNullOrEmpty(pClean)) return 0;

        // Exact match (setelah normalisasi)
        if (string.Equals(sClean, pClean, StringComparison.OrdinalIgnoreCase)) return 1000;

        // Contains match
        if (pClean.Contains(sClean, StringComparison.OrdinalIgnoreCase) || sClean.Contains(pClean, StringComparison.OrdinalIgnoreCase)) return 600;

        // Space-stripped match: "(4)AZKIATUNISA" → "azkiatunisa" vs "azkia tunisa" → "azkiatunisa"
        // Kasus nama di PSD digabung tanpa spasi
        var sNoSpace = sClean.Replace(" ", "");
        var pNoSpace = pClean.Replace(" ", "");
        if (string.Equals(sNoSpace, pNoSpace, StringComparison.OrdinalIgnoreCase)) return 950;
        if (pNoSpace.Contains(sNoSpace, StringComparison.OrdinalIgnoreCase) || sNoSpace.Contains(pNoSpace, StringComparison.OrdinalIgnoreCase)) return 580;

        // Token overlap
        var sTokens = new HashSet<string>(sClean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var pTokens = new HashSet<string>(pClean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        int overlap = sTokens.Intersect(pTokens).Count();
        if (overlap >= 2) return 400 + overlap * 50;
        // Single token yang cukup panjang juga dianggap match (misal nama 1 kata)
        if (overlap == 1 && sTokens.Union(pTokens).Count() <= 3) return 350;

        return 0;
    }

    public PhotoMatchResult FindBestMatch(string studentName, List<string> photoFiles, int threshold = 62, bool forcePick = false)
    {
        var candidates = photoFiles
            .Select(f => (Path: f, Normalized: NormalizePersonName(Path.GetFileNameWithoutExtension(f))))
            .ToList();

        var ranked = candidates
            .Select(c => (Score: Score(studentName, c.Normalized), c.Path))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(studentName) || string.IsNullOrEmpty(studentName.Trim()))
        {
            return new PhotoMatchResult { StudentName = studentName, Status = "NAMA KOSONG", Score = 0 };
        }

        if (ranked.Count == 0 || ranked[0].Score < threshold)
        {
            int lowScore = 0;
            string? lowPath = null;
            if (ranked.Count > 0)
            {
                lowScore = ranked[0].Score;
                lowPath = ranked[0].Path;
            }
            return new PhotoMatchResult
            {
                StudentName = studentName,
                MatchedFilePath = lowPath,
                Score = lowScore,
                IsPassed = false,
                Status = "TIDAK ADA",
                Note = "Skor di bawah batas"
            };
        }

        var bestScore = ranked[0].Score;
        var bestPath = ranked[0].Path;
        var secondScore = ranked.Count > 1 ? ranked[1].Score : 0;

        bool ambiguous = secondScore >= threshold && bestScore - secondScore <= 3;
        string status;
        string note = string.Empty;

        if (ambiguous)
        {
            status = "AMBIGU";
            note = $"Kandidat kedua terlalu dekat ({secondScore})";
        }
        else if (bestScore == 100)
        {
            status = "PERSIS";
        }
        else
        {
            status = "COCOK";
        }

        return new PhotoMatchResult
        {
            StudentName = studentName,
            MatchedFilePath = bestPath,
            Score = bestScore,
            IsPassed = true,
            Status = status,
            Note = note,
            BestCandidateName = Path.GetFileName(bestPath),
            BestCandidatePath = bestPath
        };
    }

    public List<PhotoMatchResult> MatchAll(List<string> names, List<string> photoFiles, int threshold = 62)
    {
        var results = new List<PhotoMatchResult>();
        var chosen = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < names.Count; i++)
        {
            var result = FindBestMatch(names[i], photoFiles, threshold);
            results.Add(result);

            if (!string.IsNullOrEmpty(result.MatchedFilePath))
            {
                var key = result.MatchedFilePath.ToLowerInvariant();
                if (!chosen.ContainsKey(key)) chosen[key] = new List<int>();
                chosen[key].Add(i);
            }
        }

        foreach (var kv in chosen)
        {
            if (kv.Value.Count <= 1) continue;
            foreach (var idx in kv.Value)
            {
                results[idx].Status = "FOTO GANDA";
                results[idx].Note = "Foto yang sama terpilih untuk lebih dari satu data";
            }
        }

        return results;
    }
}


