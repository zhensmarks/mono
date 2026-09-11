using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class TransformWindow : Window
{
    private readonly TransformService _transformService;
    private readonly LocalAIService _aiService;
    private readonly MantraDataSettings _settings;
    private readonly List<string> _headers;
    private readonly List<List<string>> _sampleRows;
    private readonly List<TransformPreset> _presets;

    private readonly List<string> _chosen = new();
    private readonly List<(string Column, string Prefix, string Separator)> _layout = new();

    public string SelectedPresetCode { get; private set; } = "";
    public string TargetHeader { get; private set; } = "";
    public string Separator { get; private set; } = " ";
    public bool KeepSources { get; private set; }
    public List<MergeColumn> MergeColumns { get; private set; } = new();
    public bool Confirmed { get; private set; }

    public TransformWindow(
        List<string> headers,
        List<List<string>> sampleRows,
        TransformService transformService,
        LocalAIService aiService,
        MantraDataSettings settings,
        IEnumerable<string>? preselected = null)
    {
        InitializeComponent();

        _headers = headers;
        _sampleRows = sampleRows;
        _transformService = transformService;
        _aiService = aiService;
        _settings = settings;

        AiEnabledCheckBox.IsChecked = settings.AiEnabled;
        AiEndpointTextBox.Text = settings.AiEndpoint;
        AiModelTextBox.Text = settings.AiModel;

        _presets = transformService.GetAvailablePresets(headers);
        PresetListBox.ItemsSource = _presets;
        if (_presets.Count > 0)
            PresetListBox.SelectedIndex = 0;

        var pre = preselected?.Where(c => headers.Contains(c)).ToList() ?? new List<string>();
        _chosen.AddRange(pre.Distinct());
        foreach (var c in _chosen)
        {
            _layout.Add((c, "", ""));
        }
        if (_chosen.Count == 0 && headers.Count >= 2)
        {
            _chosen.AddRange(headers.Take(2));
            foreach (var c in _chosen) _layout.Add((c, "", ""));
        }

        RebuildColumnBadges();
        UpdateAiButtonState();
        UpdatePreview();
    }

    private List<int> SelectedIndexes =>
        _layout.Select(item => _headers.IndexOf(item.Column))
            .Where(i => i >= 0)
            .ToList();

    private void RebuildColumnBadges()
    {
        if (PanelSelectedColumns == null) return;
        PanelSelectedColumns.Children.Clear();

        foreach (var (col, prefix, sep) in _layout)
        {
            var labelText = string.IsNullOrEmpty(prefix) ? col : $"{prefix}{col}";
            var border = new Border
            {
                Background = SolidColorBrush.Parse("#1E2433"),
                BorderBrush = SolidColorBrush.Parse("#2E384D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 6, 2),
                Padding = new Thickness(8, 3, 6, 3)
            };

            var sp = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock
            {
                Text = labelText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("#EDEDED"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            var btnRemove = new Button
            {
                Content = "x",
                FontSize = 9,
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = SolidColorBrush.Parse("#828896"),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = col
            };
            btnRemove.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is string cName)
                {
                    _layout.RemoveAll(item => item.Column == cName);
                    _chosen.RemoveAll(c => c == cName);
                    RebuildColumnBadges();
                    UpdatePreview();
                }
            };
            sp.Children.Add(btnRemove);

            border.Child = sp;
            PanelSelectedColumns.Children.Add(border);
        }

        var remaining = _headers.Except(_layout.Select(item => item.Column)).ToList();
        if (remaining.Count > 0)
        {
            var btnAdd = new Button
            {
                Content = "Tambah Kolom",
                FontSize = 11,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 2, 0, 2),
                Background = SolidColorBrush.Parse("#181B24"),
                BorderBrush = SolidColorBrush.Parse("#2E3342"),
                BorderThickness = new Thickness(1),
                Foreground = SolidColorBrush.Parse("#60A5FA"),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            var flyout = new MenuFlyout();
            foreach (var rCol in remaining)
            {
                var item = new MenuItem { Header = rCol, Tag = rCol };
                item.Click += (s, e) =>
                {
                    if (s is MenuItem mi && mi.Tag is string colToAdd)
                    {
                        _layout.Add((colToAdd, "", ""));
                        _chosen.Add(colToAdd);
                        RebuildColumnBadges();
                        UpdatePreview();
                    }
                };
                flyout.Items.Add(item);
            }

            btnAdd.Flyout = flyout;
            PanelSelectedColumns.Children.Add(btnAdd);
        }
    }

    private void OnPresetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CustomPanel == null || PresetListBox == null) return;
        if (PresetListBox.SelectedItem is TransformPreset preset)
        {
            CustomPanel.IsVisible = preset.Code == "custom";
            if (preset.Code != "custom")
            {
                TxtPreview.Text = $"Preset '{preset.Title}' siap diterapkan.";
            }
        }
    }

    private void OnAiEnabledChanged(object? sender, RoutedEventArgs e)
    {
        bool enabled = AiEnabledCheckBox.IsChecked == true;
        _settings.AiEnabled = enabled;
        _settings.Save();
        _aiService.IsEnabled = enabled;
        UpdateAiButtonState();
    }

    private void UpdateAiButtonState()
    {
        if (AiSuggestButton == null) return;
        AiSuggestButton.IsEnabled = _layout.Count >= 2;
    }

    private void SaveAiSettings()
    {
        _settings.AiEndpoint = AiEndpointTextBox.Text?.Trim() ?? "";
        _settings.AiModel = AiModelTextBox.Text?.Trim() ?? "";
        _settings.AiEnabled = AiEnabledCheckBox.IsChecked == true;
        _settings.Save();
        _aiService.IsEnabled = _settings.AiEnabled;
    }

    private async void OnAiTestClicked(object? sender, RoutedEventArgs e)
    {
        SaveAiSettings();
        AiStatusText.Text = "Menghubungi server AI lokal...";
        AiTestButton.IsEnabled = false;
        try
        {
            var models = await _aiService.ListModelsAsync(_settings.AiEndpoint);
            if (models.Count == 0)
            {
                AiStatusText.Text = "Terhubung. Model tersedia: (belum ada model).";
            }
            else
            {
                AiStatusText.Text = "Terhubung. Model tersedia: " + string.Join(", ", models.Take(6));
                if (string.IsNullOrWhiteSpace(_settings.AiModel) && models.Count > 0)
                {
                    AiModelTextBox.Text = models[0];
                    _settings.AiModel = models[0];
                    _settings.Save();
                }
            }
            UpdateAiButtonState();
        }
        catch (LocalAIException ex)
        {
            AiStatusText.Text = ex.Message;
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
    }

    private async void OnAiSuggestClicked(object? sender, RoutedEventArgs e)
    {
        SaveAiSettings();

        if (_layout.Count < 2)
        {
            AiStatusText.Text = "Pilih minimal 2 kolom sumber terlebih dahulu.";
            return;
        }

        AiSuggestButton.IsEnabled = false;

        bool aiEnabled = AiEnabledCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(_settings.AiModel);
        if (aiEnabled)
        {
            AiStatusText.Text = "Memproses...";
            try
            {
                var suggestions = await _aiService.SuggestMergeLayoutAsync(
                    _headers,
                    _sampleRows,
                    SelectedIndexes,
                    _settings.AiEndpoint,
                    _settings.AiModel);

                _layout.Clear();
                _layout.AddRange(suggestions.Select(s => (s.Header, s.Prefix, s.Separator)));
                _chosen.Clear();
                _chosen.AddRange(_layout.Select(item => item.Column));

                RebuildColumnBadges();
                UpdatePreview();

                var summary = string.Join(" | ", _layout.Select(item =>
                    string.IsNullOrEmpty(item.Prefix)
                        ? item.Column
                        : $"{item.Prefix}{item.Column}"));
                AiStatusText.Text = "Saran AI diterapkan: " + summary;
                UpdateAiButtonState();
                return;
            }
            catch (LocalAIException ex)
            {
                AiStatusText.Text = ex.Message + " Saran offline digunakan.";
            }
        }

        ApplyOfflineSuggestion();
        RebuildColumnBadges();
        UpdatePreview();
        UpdateAiButtonState();
        if (string.IsNullOrEmpty(AiStatusText.Text) || !AiStatusText.Text.Contains("offline"))
            AiStatusText.Text = "Saran offline digunakan.";
    }

    private void ApplyOfflineSuggestion()
    {
        for (int i = 0; i < _layout.Count; i++)
        {
            var col = _layout[i].Column;
            var key = NormalizeHeaderKey(col);
            var prefix = "";
            var separator = "\n";
            if (key == "nisn") prefix = "NISN : ";
            else if (key == "jabatan") prefix = "Jabatan : ";
            else if (key.Contains("tempat") && key.Contains("tanggal")) prefix = "TTL : ";
            else if (key == "tempat lahir") { prefix = "TTL : "; separator = ", "; }
            else if (key == "alamat") prefix = "Alamat : ";
            if (i == _layout.Count - 1) separator = "";
            _layout[i] = (col, prefix, separator);
        }
    }

    private static string NormalizeHeaderKey(string header)
    {
        var text = Regex.Replace(header.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return text switch
        {
            "tempat" => "tempat lahir",
            "tempat lahir" => "tempat lahir",
            "tanggal lahir" or "tgl lahir" => "tanggal lahir",
            "tempat tgl lahir" or "tempat tanggal lahir" => "tempat, tanggal lahir",
            _ => text
        };
    }

    private void UpdatePreview()
    {
        if (TxtPreview == null || TargetHeaderTextBox == null) return;

        TargetHeaderTextBox.Text = string.IsNullOrEmpty(TargetHeaderTextBox.Text)
            ? string.Join(" + ", _layout.Select(item => item.Column))
            : TargetHeaderTextBox.Text;

        if (_layout.Count < 2)
        {
            TxtPreview.Text = "Pilih minimal 2 kolom";
            return;
        }

        var vals = _layout.Select(item =>
        {
            var idx = _headers.IndexOf(item.Column);
            var value = idx >= 0 && _sampleRows.Count > 0 && idx < _sampleRows[0].Count
                ? _sampleRows[0][idx] ?? ""
                : item.Column;
            return string.IsNullOrEmpty(item.Prefix) ? value.Trim() : item.Prefix + value.Trim();
        }).Where(v => !string.IsNullOrEmpty(v)).ToList();

        var sep = GetValidSeparator() ?? " ";
        TxtPreview.Text = string.Join(sep == "\n" ? " [Baris Baru] " : sep, vals);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInput()) return;

        if (PresetListBox.SelectedItem is TransformPreset preset)
        {
            SelectedPresetCode = preset.Code;
            if (preset.Code == "custom")
            {
                TargetHeader = TargetHeaderTextBox.Text?.Trim() ?? "";
                KeepSources = KeepSourcesCheckBox.IsChecked == true;
                Separator = GetValidSeparator() ?? " ";
                MergeColumns = _layout.Select(item => new MergeColumn
                {
                    SourceIndex = _headers.IndexOf(item.Column),
                    Prefix = item.Prefix,
                    Separator = string.IsNullOrEmpty(item.Separator) ? Separator : item.Separator
                }).Where(m => m.SourceIndex >= 0).ToList();
            }
        }

        SaveAiSettings();
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private string? GetValidSeparator()
    {
        if (SeparatorComboBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? " ";
        return " ";
    }

    private bool ValidateInput()
    {
        if (PresetListBox.SelectedItem is not TransformPreset preset)
        {
            AiStatusText.Text = "Pilih preset transformasi terlebih dahulu.";
            return false;
        }

        if (preset.Code == "custom")
        {
            if (string.IsNullOrWhiteSpace(TargetHeaderTextBox.Text))
            {
                AiStatusText.Text = "Masukkan nama header hasil.";
                return false;
            }
            if (_layout.Count == 0)
            {
                AiStatusText.Text = "Pilih minimal satu kolom sumber di tabel utama.";
                return false;
            }
        }

        return true;
    }
}