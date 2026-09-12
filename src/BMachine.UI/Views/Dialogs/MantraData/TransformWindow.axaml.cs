using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class TransformWindow : MantraDialogBase
{
    private readonly TransformService _transformService;
    private readonly LocalAIService _aiService;
    private readonly MantraDataSettings _settings;
    private readonly List<string> _headers;
    private readonly List<List<string>> _sampleRows;
    private readonly ObservableCollection<LayerItemView> _layers = new();

    public List<LayerTransformSpec> Layers { get; private set; } = new();
    public bool Confirmed { get; private set; }

    public event Action? PreviewChanged;

    private bool _loading;
    private bool _suppressPreview;
    private int _currentIndex = -1;

    public TransformWindow(
        List<string> headers,
        List<List<string>> sampleRows,
        TransformService transformService,
        LocalAIService aiService,
        MantraDataSettings settings,
        IEnumerable<string>? preselected = null)
    {
        System.Diagnostics.Debug.WriteLine($"[TransformWindow] Headers count: {headers.Count}");
        
        _loading = true;
        InitializeComponent();
        _loading = false;
        DataContext = _layers;

        _headers = headers;
        _sampleRows = sampleRows;
        _transformService = transformService;
        _aiService = aiService;
        _settings = settings;

        var pre = preselected?.Where(headers.Contains).Distinct().ToList() ?? new List<string>();
        var initial = new LayerItemView("HASIL 1")
        {
            DefaultSeparator = "\n"
        };
        foreach (var header in headers)
        {
            initial.Entries.Add(new ColumnEntryItem(headers.IndexOf(header), header)
            {
                IsUsed = pre.Contains(header)
            });
        }
        
        System.Diagnostics.Debug.WriteLine($"[TransformWindow] Initial layer entries count: {initial.Entries.Count}");
        
        _layers.Add(initial);

        Subscribe(initial);
        TxtLayerName.TextChanged += (_, _) => RaisePreview();
        CmbDefaultSeparator.SelectionChanged += (_, _) =>
        {
            if (CurrentLayer is { } layer && CmbDefaultSeparator.SelectedItem is SeparatorOption opt)
                layer.DefaultSeparator = opt.Value;
            RaisePreview();
        };

        AddDefaultSeparators();
        LoadLayer(0);
    }

    private void AddDefaultSeparators()
    {
        CmbDefaultSeparator.ItemsSource = new List<SeparatorOption>
        {
            new("Baris baru", "\n"),
            new("Spasi", " "),
            new("Koma + spasi", ", "),
            new("Garis miring", " / "),
            new("Strip", " - "),
            new("Tanpa pemisah", "")
        };
    }

    private void OnLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || LstLayers is null) return;
        _suppressPreview = true;
        try
        {
            LoadLayer(LstLayers.SelectedIndex);
        }
        finally
        {
            _suppressPreview = false;
        }
    }

    private void LoadLayer(int index)
    {
        if (index < 0 || index >= _layers.Count) return;
        _currentIndex = index;
        var layer = _layers[index];
        RightPanel.DataContext = layer;
        DgEntries.ItemsSource = layer.Entries;
        TxtLayerName.Text = layer.Name;
        SelectDefaultSeparator(layer.DefaultSeparator);
        AiStatusText.Text = "";
    }

    private void SelectDefaultSeparator(string value)
    {
        foreach (var item in CmbDefaultSeparator.Items)
        {
            if (item is SeparatorOption opt && opt.Value == value)
            {
                CmbDefaultSeparator.SelectedItem = item;
                return;
            }
        }
        CmbDefaultSeparator.SelectedItem = CmbDefaultSeparator.Items.Cast<SeparatorOption>().FirstOrDefault();
    }

    private LayerItemView? CurrentLayer =>
        _currentIndex >= 0 && _currentIndex < _layers.Count ? _layers[_currentIndex] : null;

    private void Subscribe(LayerItemView layer)
    {
        foreach (var entry in layer.Entries)
            entry.PropertyChanged += OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e) => RaisePreview();

    private void RaisePreview()
    {
        if (_loading || _suppressPreview) return;
        PreviewChanged?.Invoke();
    }

    public List<LayerTransformSpec> BuildSpecs()
    {
        if (CurrentLayer != null)
        {
            CurrentLayer.DefaultSeparator = GetDefaultSeparatorValue();
            CurrentLayer.Name = TxtLayerName.Text;
        }

        return _layers.Select(layer => new LayerTransformSpec
        {
            Name = (layer.Name ?? "").Trim(),
            Separator = ResolveValue(layer.DefaultSeparator),
            Entries = layer.Entries.Select(entry => new LayerTransformEntry
            {
                SourceIndex = entry.SourceIndex,
                Header = entry.Header,
                Use = entry.IsUsed,
                Prefix = entry.Prefix,
                Separator = ResolveValue(entry.Separator)
            }).ToList()
        }).ToList();
    }

    private void OnAddLayerClicked(object? sender, RoutedEventArgs e)
    {
        var layer = new LayerItemView($"LAYER {_layers.Count + 1}")
        {
            DefaultSeparator = "\n"
        };
        foreach (var header in _headers)
        {
            layer.Entries.Add(new ColumnEntryItem(_headers.IndexOf(header), header));
        }
        _layers.Add(layer);
        Subscribe(layer);
        _loading = true;
        try
        {
            LstLayers.SelectedIndex = _layers.Count - 1;
        }
        finally
        {
            _loading = false;
        }
        LoadLayer(_layers.Count - 1);
    }

    private void OnRemoveLayerClicked(object? sender, RoutedEventArgs e)
    {
        if (_layers.Count <= 1)
        {
            AiStatusText.Text = "Minimal harus ada satu layer hasil.";
            return;
        }
        var index = LstLayers.SelectedIndex;
        if (index < 0) return;
        _layers.RemoveAt(index);
        _currentIndex = -1;
        _loading = true;
        try
        {
            LstLayers.SelectedIndex = Math.Min(index, _layers.Count - 1);
        }
        finally
        {
            _loading = false;
        }
        LoadLayer(LstLayers.SelectedIndex);
    }

    private List<ColumnEntryItem> SelectedEntries(LayerItemView layer) =>
        layer.Entries.Where(e => e.IsUsed).ToList();

    private async void OnSuggestClicked(object? sender, RoutedEventArgs e)
    {
        var layer = CurrentLayer;
        if (layer == null) return;

        var selected = SelectedEntries(layer);
        if (selected.Count == 0)
        {
            AiStatusText.Text = "Centang dahulu kolom sumber untuk layer ini.";
            return;
        }

        bool aiEnabled = _settings.AiEnabled && !string.IsNullOrWhiteSpace(_settings.AiModel);
        if (aiEnabled)
        {
            AiStatusText.Text = "Memproses...";
            BtnSuggest.IsEnabled = false;
            try
            {
                var suggestions = await _aiService.SuggestMergeLayoutAsync(
                    _headers,
                    _sampleRows,
                    selected.Select(c => c.SourceIndex).ToList(),
                    _settings.AiProvider,
                    _settings.AiEndpoint,
                    _settings.AiApiKey,
                    _settings.AiModel);

                var bySource = layer.Entries.ToDictionary(c => c.SourceIndex, c => c);
                var ordered = new List<ColumnEntryItem>();
                foreach (var s in suggestions)
                {
                    if (bySource.TryGetValue(s.SourceIndex, out var entry))
                    {
                        entry.Prefix = s.Prefix;
                        entry.Separator = ToToken(s.Separator);
                        ordered.Add(entry);
                    }
                }
                ordered.AddRange(layer.Entries.Where(c => !ordered.Contains(c)));

                layer.Entries.Clear();
                foreach (var entry in ordered)
                    layer.Entries.Add(entry);

                AiStatusText.Text = "Saran AI diterapkan.";
            }
            catch (LocalAIException ex)
            {
                AiStatusText.Text = ex.Message + " Saran offline digunakan.";
                ApplyOfflineFormat(layer, selected);
            }
            finally
            {
                BtnSuggest.IsEnabled = true;
            }
        }
        else
        {
            AiStatusText.Text = "Saran offline digunakan.";
            ApplyOfflineFormat(layer, selected);
        }

        RaisePreview();
    }

    private void ApplyOfflineFormat(LayerItemView layer, List<ColumnEntryItem> selected)
    {
        for (int position = 0; position < selected.Count; position++)
        {
            var entry = selected[position];
            string prefix = "";
            string separator = TokenFromValue("\n");
            var header = CleanHeader(entry.Header);

            if (header == "nisn") prefix = "NISN : ";
            else if (header == "nis") prefix = "NIS : ";
            else if (header == "jabatan") prefix = "Jabatan : ";
            else if (header.Contains("tempat") && header.Contains("tanggal")) prefix = "TTL : ";
            else if (header == "tempat lahir") { prefix = "TTL : "; separator = TokenFromValue(", "); }
            else if (header == "alamat") prefix = "Alamat : ";
            else if (header == "rt") { prefix = "RT "; separator = TokenFromValue(", "); }
            else if (header == "rw") { prefix = "RW "; separator = TokenFromValue(", "); }
            else if (new[] { "kelurahan", "kecamatan", "kode pos", "dusun" }.Any(key => header == key))
                separator = TokenFromValue(", ");

            if (position == selected.Count - 1) separator = TokenFromValue("");

            entry.Prefix = prefix;
            entry.Separator = separator;
        }
    }

    private async void OnAutoArrangeClicked(object? sender, RoutedEventArgs e)
    {
        await RunAutoArrange(true);
    }

    private async void OnArrangeManualClicked(object? sender, RoutedEventArgs e)
    {
        await RunAutoArrange(false);
    }

    private async Task RunAutoArrange(bool useAi)
    {
        var normalized = _headers.Select(CleanHeader).ToList();
        int noIndex = normalized.FindIndex(h => h == "no");
        int nameIndex = normalized.FindIndex(h => h is "nama" or "nama lengkap");
        var identity = new List<int>();
        if (noIndex >= 0) identity.Add(noIndex);
        if (nameIndex >= 0) identity.Add(nameIndex);
        var used = identity.ToHashSet();

        var specs = new List<(string Name, string Separator, HashSet<int> Sources)>();
        if (identity.Count > 0)
        {
            var layerName = identity.Count > 1 ? "IDENTITAS" : nameIndex >= 0 ? "NAMA" : "NO";
            specs.Add((layerName, " ", identity.ToList().ToHashSet()));
        }
        var remaining = Enumerable.Range(0, _headers.Count).Where(i => !used.Contains(i)).ToList();
        if (remaining.Count > 0)
        {
            specs.Add(("DATA", "\n", remaining.ToHashSet()));
        }
        if (specs.Count == 0)
        {
            AiStatusText.Text = "Tidak ada kolom untuk diatur.";
            return;
        }

        var newLayers = new List<LayerItemView>();
        foreach (var (name, separator, sources) in specs)
        {
            var layer = new LayerItemView(name) { DefaultSeparator = separator };
            foreach (var header in _headers)
            {
                var index = _headers.IndexOf(header);
                layer.Entries.Add(new ColumnEntryItem(index, header) { IsUsed = sources.Contains(index) });
            }
            newLayers.Add(layer);
        }

        foreach (var layer in newLayers)
        {
            if (int.TryParse(layer.Name, out _))
                continue;
            if (layer.Name == "IDENTITAS" || layer.Name == "NAMA" || layer.Name == "NO")
            {
                var selected = SelectedEntries(layer);
                for (int position = 0; position < selected.Count; position++)
                    selected[position].Separator = position < selected.Count - 1 ? TokenFromValue(" ") : TokenFromValue("");
            }
            else
            {
                ApplyOfflineFormat(layer, SelectedEntries(layer));
            }
        }

        bool doAi = useAi && _settings.AiEnabled && !string.IsNullOrWhiteSpace(_settings.AiModel);
        if (doAi)
        {
            AiStatusText.Text = "Memproses...";
            BtnAutoArrange.IsEnabled = false;
            try
            {
                foreach (var layer in newLayers)
                {
                    var selected = SelectedEntries(layer);
                    if (selected.Count == 0) continue;
                    var suggestions = await _aiService.SuggestMergeLayoutAsync(
                        _headers,
                        _sampleRows,
                        selected.Select(c => c.SourceIndex).ToList(),
                        _settings.AiProvider,
                        _settings.AiEndpoint,
                        _settings.AiApiKey,
                        _settings.AiModel);

                    var bySource = layer.Entries.ToDictionary(c => c.SourceIndex, c => c);
                    var ordered = new List<ColumnEntryItem>();
                    foreach (var s in suggestions)
                    {
                        if (bySource.TryGetValue(s.SourceIndex, out var entry))
                        {
                            entry.Prefix = s.Prefix;
                            entry.Separator = ToToken(s.Separator);
                            ordered.Add(entry);
                        }
                    }
                    ordered.AddRange(layer.Entries.Where(c => !ordered.Contains(c)));
                    layer.Entries.Clear();
                    foreach (var entry in ordered)
                        layer.Entries.Add(entry);
                }
                AiStatusText.Text = "Susunan otomatis AI diterapkan.";
            }
            catch (LocalAIException ex)
            {
                AiStatusText.Text = ex.Message + " Susunan otomatis offline tetap digunakan.";
            }
            finally
            {
                BtnAutoArrange.IsEnabled = true;
            }
        }
        else
        {
            AiStatusText.Text = "Susunan manual diterapkan.";
        }

        foreach (var layer in newLayers)
            Subscribe(layer);

        _layers.Clear();
        foreach (var layer in newLayers)
            _layers.Add(layer);
        _currentIndex = -1;
        _loading = true;
        try
        {
            LstLayers.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }
        LoadLayer(0);
        RaisePreview();
    }

    private void OnAiSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dlg = new AiSettingsWindow(_aiService, _settings);
        dlg.ShowDialog(this);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        var specs = BuildSpecs();

        try
        {
            _transformService.BuildLayerSheet(_headers, BuildSampleRows(), specs);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            AiStatusText.Text = "Rencana layer belum lengkap: " + ex.Message;
            return;
        }

        Layers = specs;
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private List<TableDataRow> BuildSampleRows()
    {
        return _sampleRows.Select((values, i) =>
        {
            var row = new TableDataRow { RowNumber = i + 1 };
            for (int c = 0; c < _headers.Count && c < values.Count; c++)
                row[_headers[c]] = values[c] ?? "";
            return row;
        }).ToList();
    }

    private string GetDefaultSeparatorValue()
    {
        if (CmbDefaultSeparator.SelectedItem is SeparatorOption opt)
            return opt.Value;
        return "\n";
    }

    private static string CleanHeader(string header)
    {
        return Regex.Replace(header?.ToLowerInvariant() ?? "", @"[^a-z0-9]+", " ").Trim();
    }

    private static string TokenFromValue(string value) =>
        value.ToLowerInvariant() switch
        {
            "\n" => "Baris baru",
            "\\n" => "Baris baru",
            " " => "Spasi",
            ", " => "Koma + spasi",
            " / " => "Garis miring",
            " - " => "Strip",
            "" => "Tanpa pemisah",
            _ => value
        };

    private static string ToToken(string raw)
    {
        if (raw == "\n" || raw == "\\n") return "Baris baru";
        return TokenFromValue(raw);
    }

    private static string ResolveValue(string token) =>
        token switch
        {
            "Baris baru" => "\n",
            "Spasi" => " ",
            "Koma + spasi" => ", ",
            "Garis miring" => " / ",
            "Strip" => " - ",
            "Tanpa pemisah" => "",
            _ => token
        };
}