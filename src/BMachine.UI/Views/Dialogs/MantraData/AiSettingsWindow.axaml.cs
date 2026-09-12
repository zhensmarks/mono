using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class AiSettingsWindow : MantraDialogBase
{
    private readonly LocalAIService _aiService;
    private readonly MantraDataSettings _settings;
    private readonly Dictionary<string, string> _defaultEndpoints = new()
    {
        [AiProviderNames.Ollama] = "http://127.0.0.1:11434",
        [AiProviderNames.OpenAiCompatible] = "https://api.openai.com/v1",
        [AiProviderNames.NineRouter] = "http://127.0.0.1:20128/v1"
    };

    public bool Saved { get; private set; }

    public AiSettingsWindow(LocalAIService aiService, MantraDataSettings settings)
    {
        InitializeComponent();

        _aiService = aiService;
        _settings = settings;

        ChkAiEnabled.IsChecked = settings.AiEnabled;
        TxtAiEndpoint.Text = settings.AiEndpoint;
        TxtAiModel.Text = settings.AiModel;
        TxtAiApiKey.Text = settings.AiApiKey;
        SelectProvider(settings.AiProvider);
        OnProviderChanged(null, null);
    }

    private string CurrentProvider =>
        CmbAiProvider.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? AiProviderNames.Ollama
            : AiProviderNames.Ollama;

    private void SelectProvider(string provider)
    {
        foreach (var item in CmbAiProvider.Items)
        {
            if (item is ComboBoxItem providerItem &&
                string.Equals(providerItem.Tag?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            {
                CmbAiProvider.SelectedItem = providerItem;
                return;
            }
        }
    }

    private void OnProviderChanged(object? sender, SelectionChangedEventArgs? e)
    {
        bool isOpenAi = AiProviderNames.IsOpenAiCompatible(CurrentProvider);
        TxtAiApiKey.IsEnabled = isOpenAi;

        string defaultEndpoint = _defaultEndpoints.TryGetValue(CurrentProvider, out var ep) ? ep : "";
        string current = TxtAiEndpoint.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(current) || _defaultEndpoints.ContainsValue(current))
            TxtAiEndpoint.Text = defaultEndpoint;

        TxtAiEndpoint.Watermark = current is "" ? "Alamat server AI (misalnya: " + defaultEndpoint + ")" : "";
    }

    private async void OnTestClicked(object? sender, RoutedEventArgs e)
    {
        string endpoint = TxtAiEndpoint.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            TestStatusText.Text = "Alamat server belum diisi.";
            return;
        }

        TestStatusText.Text = "Menghubungi server AI...";
        BtnAiTest.IsEnabled = false;
        try
        {
            var models = await _aiService.ListModelsAsync(endpoint, CurrentProvider, TxtAiApiKey.Text?.Trim() ?? "");
            if (models.Count == 0)
            {
                TestStatusText.Text = "Terhubung. Model tersedia: (belum ada model).";
            }
            else
            {
                TestStatusText.Text = "Terhubung. Model tersedia: " + string.Join(", ", models);
                if (string.IsNullOrWhiteSpace(TxtAiModel.Text))
                    TxtAiModel.Text = models[0];
            }
        }
        catch (LocalAIException ex)
        {
            TestStatusText.Text = ex.Message;
        }
        finally
        {
            BtnAiTest.IsEnabled = true;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        bool enabled = ChkAiEnabled.IsChecked == true;
        string endpoint = TxtAiEndpoint.Text?.Trim() ?? "";
        string model = TxtAiModel.Text?.Trim() ?? "";
        if (enabled && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model)))
        {
            TestStatusText.Text = "Alamat server dan nama model wajib diisi saat AI diaktifkan.";
            return;
        }

        _settings.AiEnabled = enabled;
        _settings.AiProvider = CurrentProvider;
        _settings.AiEndpoint = endpoint;
        _settings.AiModel = model;
        _settings.AiApiKey = TxtAiApiKey.Text?.Trim() ?? "";
        _settings.Save();
        _aiService.IsEnabled = enabled;

        Saved = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close(false);
    }
}