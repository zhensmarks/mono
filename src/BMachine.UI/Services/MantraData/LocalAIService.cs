using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BMachine.UI.Services.MantraData;

public class LocalAIException : Exception
{
    public LocalAIException(string message) : base(message) { }
    public LocalAIException(string message, Exception inner) : base(message, inner) { }
}

public class LocalAIService
{
    public bool IsEnabled { get; set; }
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<List<string>> ListModelsAsync(string endpoint)
    {
        try
        {
            var url = endpoint.TrimEnd('/') + "/api/tags";
            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsElem))
            {
                foreach (var item in modelsElem.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElem))
                    {
                        var name = nameElem.GetString();
                        if (!string.IsNullOrEmpty(name))
                            models.Add(name);
                    }
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            throw new LocalAIException($"Server AI lokal tidak merespons: {ex.Message}", ex);
        }
    }

    public async Task<List<MergeSuggestionItem>> SuggestMergeLayoutAsync(
        List<string> headers,
        List<List<string>> sampleRows,
        List<int> selectedIndexes,
        string endpoint,
        string model)
    {
        var selected = selectedIndexes.Select(i => headers[i]).ToList();
        var samples = sampleRows.Take(4).Select(row =>
        {
            var dict = new Dictionary<string, string>();
            foreach (var idx in selectedIndexes)
            {
                dict[headers[idx]] = idx < row.Count ? row[idx] : "";
            }
            return dict;
        }).ToList();

        var prompt = "Anda membantu menyiapkan layer teks Photoshop untuk ID card. Pengguna sudah memilih kolom; " +
                     "jangan menambah, menghapus, mengubah nilai, atau memilih kolom lain. Tentukan urutan, prefix singkat, " +
                     "dan separator setelah setiap nilai. Separator harus salah satu: \\n, koma-spasi, spasi, atau kosong. " +
                     "Balas JSON saja: {\"items\":[{\"source\":nama_header,\"prefix\":teks,\"separator\":teks}]}. " +
                     $"Kolom terpilih: {JsonSerializer.Serialize(selected)}. " +
                     $"Contoh data: {JsonSerializer.Serialize(samples)}";

        try
        {
            var url = endpoint.TrimEnd('/') + "/api/generate";
            var payload = new
            {
                model = model,
                prompt = prompt,
                stream = false,
                format = "json"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var respJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respJson);

            var rawResponse = doc.RootElement.GetProperty("response").GetString() ?? "";
            using var responseDoc = JsonDocument.Parse(rawResponse);

            var itemsElem = responseDoc.RootElement.GetProperty("items");
            var allowed = new Dictionary<string, (int Index, string Header)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedIndexes.Count; i++)
            {
                allowed[selected[i]] = (selectedIndexes[i], selected[i]);
            }

            var output = new List<MergeSuggestionItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validSeparators = new HashSet<string> { "\n", ", ", " ", "" };

            foreach (var item in itemsElem.EnumerateArray())
            {
                var source = item.GetProperty("source").GetString() ?? "";
                if (!allowed.ContainsKey(source) || seen.Contains(source))
                    throw new LocalAIException("AI memilih kolom di luar pilihan pengguna atau menduplikasi kolom.");

                var separator = item.GetProperty("separator").GetString() ?? "";
                if (separator == "\\n") separator = "\n";
                if (!validSeparators.Contains(separator))
                    throw new LocalAIException("AI memberikan pemisah yang tidak diizinkan.");

                var (index, header) = allowed[source];
                var prefix = item.TryGetProperty("prefix", out var pElem) ? pElem.GetString() ?? "" : "";

                output.Add(new MergeSuggestionItem(index, header, prefix, separator));
                seen.Add(source);
            }

            if (seen.Count != allowed.Count)
                throw new LocalAIException("AI tidak mengembalikan semua kolom yang dipilih pengguna.");

            return output;
        }
        catch (Exception ex) when (ex is not LocalAIException)
        {
            throw new LocalAIException($"AI lokal tidak dapat dihubungi: {ex.Message}", ex);
        }
    }

    public async Task<List<DataCleaningSuggestion>> AnalyzeCleanupSuggestionsAsync(
        List<string> headers,
        List<List<string>> sampleRows,
        string endpoint,
        string model)
    {
        var actionable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "trim_spaces", "remove_repeated_headers", "drop_empty_columns", "normalize_gender"
        };

        var prompt = "Anda adalah pemeriksa kualitas data tabel untuk pembersihan. " +
                     "Hanya deteksi masalah nyata, jangan menambah atau mengubah data. " +
                     "Balas JSON saja dengan format: " +
                     "{\"suggestions\":[{\"code\":\"trim_spaces\",\"title\":\"Rapikan spasi\",\"detail\":\"Penjelasan spesifik plus contoh nilai\",\"count\":12,\"applicable\":true}]}. " +
                     "Keterangan kode: gunakan salah satu kode automatik berikut BILA masalahnya bisa diperbaiki otomatis dan aman: " +
                     "trim_spaces, remove_repeated_headers, drop_empty_columns, normalize_gender. " +
                     "Selain itu gunakan kode deskriptif unik huruf kecil seperti ai_mixed_format dan set applicable=false. " +
                     "count adalah perkiraan jumlah sel atau baris yang terpengaruh (0 jika tidak tahu). " +
                     $"Kolom: {JsonSerializer.Serialize(headers)}. " +
                     $"Contoh data: {JsonSerializer.Serialize(sampleRows)}";

        try
        {
            var url = endpoint.TrimEnd('/') + "/api/generate";
            var payload = new
            {
                model = model,
                prompt = prompt,
                stream = false,
                format = "json"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var respJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respJson);
            var rawResponse = doc.RootElement.GetProperty("response").GetString() ?? "";
            using var responseDoc = JsonDocument.Parse(rawResponse);
            var itemsElem = responseDoc.RootElement.GetProperty("suggestions");

            var output = new List<DataCleaningSuggestion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxCount = headers.Count * sampleRows.Count;

            foreach (var item in itemsElem.EnumerateArray())
            {
                var code = item.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var detail = item.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
                var applicable = item.TryGetProperty("applicable", out var a) && a.GetBoolean();
                var count = item.TryGetProperty("count", out var n) && n.TryGetInt32(out var ci) ? ci : 0;

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title) ||
                    code.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                    continue;

                if (seen.Contains(code)) continue;
                seen.Add(code);

                if (actionable.Contains(code))
                {
                    if (!applicable) continue;
                }
                else
                {
                    code = "ai_" + code;
                    applicable = false;
                }

                output.Add(new DataCleaningSuggestion
                {
                    Code = code,
                    Title = title.Trim(),
                    Detail = detail.Trim(),
                    Count = Math.Clamp(count, 0, maxCount),
                    Applicable = applicable
                });
            }

            return output;
        }
        catch (Exception ex) when (ex is not LocalAIException)
        {
            throw new LocalAIException($"AI lokal tidak dapat dihubungi: {ex.Message}", ex);
        }
    }
}

public record MergeSuggestionItem(int SourceIndex, string Header, string Prefix, string Separator);