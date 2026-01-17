using System.ComponentModel;
using System.Globalization;

namespace BMachine.SDK;

public interface ILanguageService : INotifyPropertyChanged
{
    CultureInfo CurrentLanguage { get; }
    IReadOnlyList<CultureInfo> AvailableLanguages { get; }
    
    // Get string by key
    string GetString(string key);
    
    // Indexer for easier binding
    string this[string key] { get; }

    Task InitializeAsync();
    Task SetLanguageAsync(string languageCode);
}
