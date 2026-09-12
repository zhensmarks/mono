using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BMachine.UI.Services.MantraData;

public class LocalAIException : Exception
{
    public LocalAIException(string message) : base(message) { }
    public LocalAIException(string message, Exception inner) : base(message, inner) { }
}

public static class AiProviderNames
{
    public const string Ollama = "ollama";
    public const string OpenAiCompatible = "openai";
    public const string NineRouter = "9router";

    public static bool IsOpenAiCompatible(string provider) =>
        provider is OpenAiCompatible or NineRouter;
}

public class LocalAIService
{
    public bool IsEnabled { get; set; }
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static string EndpointFor(string provider, string endpoint) =>
        endpoint.TrimEnd('/');

    private static HttpRequestMessage OpenAiRequest(string url, string apiKey, HttpMethod method, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        if (payload != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }
        return request;
    }

    public async Task<List<string>> ListModelsAsync(string endpoint, string provider, string apiKey)
    {
        try
        {
if (AiProviderNames.IsOpenAiCompatible(provider))
            {
                var url = EndpointFor(provider, endpoint) + "/models";
                using var request = OpenAiRequest(url, apiKey, HttpMethod.Get);
                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var models = new List<string>();
                if (doc.RootElement.TryGetProperty("data", out var dataElem))
                {
                    foreach (var item in dataElem.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElem))
                        {
                            var id = idElem.GetString();
                            if (!string.IsNullOrEmpty(id))
                                models.Add(id);
                        }
                    }
                }
                return models;
            }

            var ollamaUrl = EndpointFor(provider, endpoint) + "/api/tags";
            var ollamaResponse = await _client.GetAsync(ollamaUrl);
            ollamaResponse.EnsureSuccessStatusCode();
            var ollamaJson = await ollamaResponse.Content.ReadAsStringAsync();
            using var ollamaDoc = JsonDocument.Parse(ollamaJson);
            var ollamaModels = new List<string>();
            if (ollamaDoc.RootElement.TryGetProperty("models", out var modelsElem))
            {
                foreach (var item in modelsElem.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElem))
                    {
                        var name = nameElem.GetString();
                        if (!string.IsNullOrEmpty(name))
                            ollamaModels.Add(name);
                    }
                }
            }
            return ollamaModels;
        }
        catch (Exception ex)
        {
            throw new LocalAIException($"Server AI tidak merespons: {ex.Message}", ex);
        }
    }

    public async Task<List<MergeSuggestionItem>> SuggestMergeLayoutAsync(
        List<string> headers,
        List<List<string>> sampleRows,
        List<int> selectedIndexes,
        string provider,
        string endpoint,
        string apiKey,
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
                     "dan separator setelah setiap nilai. Separator harus salah satu: baris-baru, koma-spasi, spasi, atau kosong. " +
                     "Balas JSON saja dengan format: {\"items\":[{\"source\":nama_header,\"prefix\":teks,\"separator\":teks}]} " +
                     $"Kolom terpilih: {JsonSerializer.Serialize(selected)}. " +
                     $"Contoh data: {JsonSerializer.Serialize(samples)}";

        try
        {
            var rawResponse = await ChatAsync(provider, endpoint, apiKey, model, prompt);

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
                if (separator == "baris-baru" || separator == "baris baru" || separator == "newline") separator = "\n";
                if (separator == "koma-spasi" || separator == "comma" || separator == "koma") separator = ", ";
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
            throw new LocalAIException($"AI tidak dapat dihubungi: {ex.Message}", ex);
        }
    }

    public async Task<List<DataCleaningSuggestion>> AnalyzeCleanupSuggestionsAsync(
        List<string> headers,
        List<List<string>> sampleRows,
        string provider,
        string endpoint,
        string apiKey,
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
            var rawResponse = await ChatAsync(provider, endpoint, apiKey, model, prompt);
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
            throw new LocalAIException($"AI tidak dapat dihubungi: {ex.Message}", ex);
        }
    }

    private async Task<string> ChatAsync(string provider, string endpoint, string apiKey, string model, string prompt)
    {
        if (AiProviderNames.IsOpenAiCompatible(provider))
        {
            var url = EndpointFor(provider, endpoint) + "/chat/completions";
            var payload = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = "Anda adalah asisten penyusun layer teks dan pemeriksa kualitas data. Selalu jujur dan konsisten, balas sesuai format yang diminta." },
                    new { role = "user", content = prompt }
                },
                temperature = 0,
                response_format = new { type = "json_object" }
            };

            using var request = OpenAiRequest(url, apiKey, HttpMethod.Post, payload);
            var response = await _client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new LocalAIException($"API mengembalikan {(int)response.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                throw new LocalAIException("API tidak mengembalikan pilihan jawaban.");
            var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return ExtractJsonObject(content);
        }

        var urlOllama = EndpointFor(provider, endpoint) + "/api/generate";
        var payloadOllama = new
        {
            model = model,
            prompt = prompt,
            stream = false,
            format = "json"
        };
        var contentOllama = new StringContent(JsonSerializer.Serialize(payloadOllama), Encoding.UTF8, "application/json");
        var responseOllama = await _client.PostAsync(urlOllama, contentOllama);
        responseOllama.EnsureSuccessStatusCode();
        var respJson = await responseOllama.Content.ReadAsStringAsync();
        using var docOllama = JsonDocument.Parse(respJson);
        return docOllama.RootElement.GetProperty("response").GetString() ?? "";
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```"))
        {
            var marker = trimmed.IndexOf('\n');
            if (marker >= 0) trimmed = trimmed[(marker + 1)..];
            var end = trimmed.LastIndexOf("```");
            if (end >= 0) trimmed = trimmed[..end];
        }
        trimmed = trimmed.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return trimmed[firstBrace..(lastBrace + 1)];
        return trimmed;
    }
}

public record MergeSuggestionItem(int SourceIndex, string Header, string Prefix, string Separator);