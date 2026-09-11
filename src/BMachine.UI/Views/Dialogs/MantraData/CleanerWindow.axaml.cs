using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BMachine.UI.Models.MantraData;
using BMachine.UI.Services.MantraData;

namespace BMachine.UI.Views.Dialogs.MantraData;

public partial class CleanerWindow : Window
{
    public ObservableCollection<CleanerSuggestionItem> Suggestions { get; } = new();
    public List<string> SelectedCodes { get; private set; } = new();
    public bool Applied { get; private set; }

    public CleanerWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public CleanerWindow(List<DataCleaningSuggestion> suggestions) : this()
    {
        foreach (var suggestion in suggestions)
        {
            Suggestions.Add(new CleanerSuggestionItem
            {
                Code = suggestion.Code,
                Title = suggestion.Title,
                Detail = suggestion.Detail,
                Count = suggestion.Count,
                Applicable = suggestion.Applicable,
                IsSelected = suggestion.Applicable
            });
        }

        EmptyStatePanel.IsVisible = Suggestions.Count == 0;
    }

    private void OnPreviewClicked(object? sender, RoutedEventArgs e)
    {
        SelectedCodes = Suggestions.Where(s => s.IsSelected).Select(s => s.Code).ToList();
        Applied = false;
        Close();
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        SelectedCodes = Suggestions.Where(s => s.IsSelected).Select(s => s.Code).ToList();
        Applied = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        SelectedCodes = new List<string>();
        Applied = false;
        Close();
    }
}

public class CleanerSuggestionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Count { get; set; }
    public bool Applicable { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}