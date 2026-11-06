using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BMachine.UI.ViewModels;

/// <summary>
/// Pengaturan untuk `Scripts/Action/save_master.jsx` (Script Manager -> tab SAVE MASTER).
///
/// Satu sumber data: `%APPDATA%\BMachine.v2\save_master_config.json`. File itu yang dibaca
/// JSX lewat `$.getenv("APPDATA")`, jadi tidak ada nilai yang disimpan di dua tempat.
///
/// Isinya hanya "langkah otomatis" yang dijalankan saat Save. Pilihan output per-save
/// (PSD/JPG/PNG/pas foto) tetap dipilih user di dialog Photoshop dan TIDAK diatur di sini.
/// </summary>
public partial class SaveMasterSettingsViewModel : ObservableObject
{
    public const string ConfigFileName = "save_master_config.json";
    private const string ConfigFolderName = "BMachine.v2";

    /// <summary>Kalimat terlarang bawaan, sama dengan bawaan JSX saat file belum ada.</summary>
    public const string DefaultRedaksi =
        "TK DELAPAN MATA AIR\n" +
        "TAAM AISYAH MIFTAHUL KHAER\n" +
        "JL. BIMA NO. 10 RT. 04 RW. 04  KEL. ARJUNA KEC. CICENDO\n" +
        "Jl. Sari Endah No. 7A Gegerkalong Hilir Bandung\n" +
        "Jl. Sari Endah No.7A, Sukarasa, Kec. Sukasari Kota Bandung, Jaw";

    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ConfigFolderName,
        ConfigFileName);

    // --- Langkah otomatis saat save -----------------------------------------
    [ObservableProperty] private bool _antiRamijud = true;
    [ObservableProperty] private bool _redaksiCheck = true;
    [ObservableProperty] private bool _closeAfterSave = true;
    [ObservableProperty] private bool _crop2x3 = true;
    [ObservableProperty] private bool _crop4x6 = true;
    [ObservableProperty] private bool _batchAllDocs = true;

    [ObservableProperty] private string _redaksiText = DefaultRedaksi;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isStatusVisible;

    public string ConfigPathLabel => DefaultConfigPath;

    public string RedaksiSummary
    {
        get
        {
            int n = RedaksiLines.Count;
            return n == 0 ? "Belum ada kalimat terlarang." : $"{n} kalimat terlarang aktif.";
        }
    }

    public List<string> RedaksiLines => RedaksiText
        .Split('\n')
        .Select(l => l.Replace("\r", "").Trim())
        .Where(l => l.Length > 0)
        .ToList();

    public void Load(string? path = null)
    {
        string file = path ?? DefaultConfigPath;
        try
        {
            if (!File.Exists(file))
            {
                RedaksiText = DefaultRedaksi;
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;

            if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Object)
            {
                AntiRamijud = ReadBool(steps, "antiRamijud");
                RedaksiCheck = ReadBool(steps, "redaksiCheck");
                CloseAfterSave = ReadBool(steps, "closeAfterSave");
                Crop2x3 = ReadBool(steps, "crop2x3");
                Crop4x6 = ReadBool(steps, "crop4x6");
                BatchAllDocs = ReadBool(steps, "batchAllDocs");
            }

            if (root.TryGetProperty("redaksi", out var red) && red.ValueKind == JsonValueKind.Array)
            {
                var lines = red.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? "")
                    .Where(x => x.Trim().Length > 0)
                    .ToList();
                RedaksiText = lines.Count > 0 ? string.Join("\n", lines) : "";
            }
        }
        catch
        {
            // Config rusak jangan bikin pengaturan tidak bisa dibuka: pakai bawaan.
        }
        OnPropertyChanged(nameof(RedaksiSummary));
    }

    [RelayCommand]
    private void Save() => SaveTo(null);

    /// <summary>Menulis config dan mengembalikan pesan status (untuk dipakai UI maupun test).</summary>
    public string SaveTo(string? path)
    {
        string file = path ?? DefaultConfigPath;
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var payload = new ConfigDto
            {
                Version = 1,
                Steps = new StepsDto
                {
                    AntiRamijud = AntiRamijud,
                    RedaksiCheck = RedaksiCheck,
                    CloseAfterSave = CloseAfterSave,
                    Crop2x3 = Crop2x3,
                    Crop4x6 = Crop4x6,
                    BatchAllDocs = BatchAllDocs
                },
                Redaksi = RedaksiLines
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };
            File.WriteAllText(file, JsonSerializer.Serialize(payload, options), new System.Text.UTF8Encoding(false));

            IsStatusVisible = true;
            StatusMessage = "Pengaturan Save Master disimpan. Berlaku pada save berikutnya.";
            OnPropertyChanged(nameof(RedaksiSummary));
            return StatusMessage;
        }
        catch (Exception ex)
        {
            IsStatusVisible = true;
            StatusMessage = "Gagal menyimpan pengaturan: " + ex.Message;
            return StatusMessage;
        }
    }

    [RelayCommand]
    private void ResetRedaksi()
    {
        RedaksiText = DefaultRedaksi;
        OnPropertyChanged(nameof(RedaksiSummary));
    }

    [RelayCommand]
    private void EnableAll()
    {
        AntiRamijud = RedaksiCheck = CloseAfterSave = Crop2x3 = Crop4x6 = BatchAllDocs = true;
    }

    private static bool ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return true;
        return v.ValueKind switch
        {
            JsonValueKind.False => false,
            JsonValueKind.String => !v.GetString()!.Equals("false", StringComparison.OrdinalIgnoreCase)
                                    && v.GetString() != "0",
            JsonValueKind.Number => v.GetDouble() != 0,
            _ => true
        };
    }

    private sealed class ConfigDto
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("steps")] public StepsDto Steps { get; set; } = new();
        [JsonPropertyName("redaksi")] public List<string> Redaksi { get; set; } = new();
    }

    private sealed class StepsDto
    {
        [JsonPropertyName("antiRamijud")] public bool AntiRamijud { get; set; }
        [JsonPropertyName("redaksiCheck")] public bool RedaksiCheck { get; set; }
        [JsonPropertyName("closeAfterSave")] public bool CloseAfterSave { get; set; }
        [JsonPropertyName("crop2x3")] public bool Crop2x3 { get; set; }
        [JsonPropertyName("crop4x6")] public bool Crop4x6 { get; set; }
        [JsonPropertyName("batchAllDocs")] public bool BatchAllDocs { get; set; }
    }
}
