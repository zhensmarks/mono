using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BMachine.SDK;

namespace BMachine.UI.ViewModels;

public partial class PointLeaderboardViewModel : ObservableObject
{
    private readonly IDatabase? _database;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<LeaderboardItem> _items = new();

    [ObservableProperty]
    private ObservableCollection<LeaderboardItem> _monthlyItems = new();

    // 0 = Harian, 1 = Bulanan
    [ObservableProperty]
    private int _selectedTab = 0;

    public ObservableCollection<LeaderboardItem> ActiveItems => SelectedTab == 0 ? Items : MonthlyItems;
    public int ActiveTotalPoints => ActiveItems.Sum(x => x.Points);
    public string FormattedTotalPoints => ActiveTotalPoints.ToString("N0", new System.Globalization.CultureInfo("id-ID"));
    public bool IsHarianSelected => SelectedTab == 0;
    public bool IsBulananSelected => SelectedTab == 1;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ActiveItems));
        OnPropertyChanged(nameof(ActiveTotalPoints));
        OnPropertyChanged(nameof(FormattedTotalPoints));
        OnPropertyChanged(nameof(IsHarianSelected));
        OnPropertyChanged(nameof(IsBulananSelected));
    }

    public PointLeaderboardViewModel(IDatabase database)
    {
        _database = database;
        // Load data...
    }

    // Design-time constructor
    public PointLeaderboardViewModel()
    {
        // Design Time
        Items.Add(new LeaderboardItem { Name = "User 1", Points = 100, Rank = 1 });
    }

    [RelayCommand]
    private void SwitchToHarian()
    {
        SelectedTab = 0;
    }

    [RelayCommand]
    private void SwitchToBulanan()
    {
        SelectedTab = 1;
    }

    [RelayCommand]
    private async Task LoadData()
    {
        if (_database == null) return;
        IsLoading = true;
        Items.Clear();
        MonthlyItems.Clear();

        try
        {
            var range = await _database.GetAsync<string>("Leaderboard.Range");
            var monthlyRange = await _database.GetAsync<string>("Leaderboard.MonthlyRange");
            var sheetId = await _database.GetAsync<string>("Google.SheetId");
            var sheetName = await _database.GetAsync<string>("Google.SheetName");
            var credsPath = await _database.GetAsync<string>("Google.CredsPath");

            if (string.IsNullOrEmpty(sheetId) || string.IsNullOrEmpty(credsPath))
            {
                IsLoading = false;
                return;
            }

            Google.Apis.Auth.OAuth2.GoogleCredential credential;
            using (var stream = new System.IO.FileStream(credsPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromStream(stream).CreateScoped(Google.Apis.Sheets.v4.SheetsService.Scope.Spreadsheets);
            }

            var service = new Google.Apis.Sheets.v4.SheetsService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "BMachine",
            });

            // Load Harian (Daily)
            if (!string.IsNullOrEmpty(range))
            {
                await LoadRangeInto(service, sheetId, sheetName ?? "Sheet1", range, Items);
            }

            // Load Bulanan (Monthly)
            if (!string.IsNullOrEmpty(monthlyRange))
            {
                await LoadRangeInto(service, sheetId, sheetName ?? "Sheet1", monthlyRange, MonthlyItems);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Leaderboard Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ActiveItems));
            OnPropertyChanged(nameof(ActiveTotalPoints));
            OnPropertyChanged(nameof(FormattedTotalPoints));
        }
    }

    private async Task LoadRangeInto(
        Google.Apis.Sheets.v4.SheetsService service, 
        string sheetId, string sheetName, string range, 
        ObservableCollection<LeaderboardItem> target)
    {
        var fullRange = $"{sheetName}!{range}";
        var request = service.Spreadsheets.Values.Get(sheetId, fullRange);
        var response = await request.ExecuteAsync();
        var values = response.Values;

        if (values != null && values.Count > 0)
        {
            int rank = 1;
            bool isFirstRow = true;

            foreach (var row in values)
            {
                if (row.Count < 1) continue;

                // Auto-skip header row (check if 2nd column is non-numeric)
                if (isFirstRow)
                {
                    isFirstRow = false;
                    if (row.Count >= 2)
                    {
                        var testStr = row[1]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(testStr) && 
                            (!char.IsDigit(testStr[0]) || testStr.Contains("TOTAL") || testStr.Contains("POIN")))
                        {
                            Console.WriteLine($"[Leaderboard] Skipping header row: {testStr}");
                            continue;
                        }
                    }
                }

                string name = row[0]?.ToString()?.Trim() ?? "Unknown";
                int points = 0;

                // Parse Points from 2nd column
                // Handle Indonesian locale: dots as thousand separators (e.g. 10.713 = 10713)
                if (row.Count >= 2)
                {
                    var pStr = row[1]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(pStr))
                    {
                        // Normalize: remove spaces
                        pStr = pStr.Replace(" ", "");
                        
                        // Detect Indonesian thousand-separator format: X.XXX or XX.XXX.XXX
                        // Pattern: dot followed by exactly 3 digits (possibly repeated)
                        if (System.Text.RegularExpressions.Regex.IsMatch(pStr, @"^\d{1,3}(\.\d{3})+$"))
                        {
                            // Dots are thousand separators — remove them
                            pStr = pStr.Replace(".", "");
                        }
                        // Also handle comma as thousand separator: 10,713
                        else if (System.Text.RegularExpressions.Regex.IsMatch(pStr, @"^\d{1,3}(,\d{3})+$"))
                        {
                            pStr = pStr.Replace(",", "");
                        }
                        
                        if (double.TryParse(pStr, System.Globalization.NumberStyles.Any, 
                                           System.Globalization.CultureInfo.InvariantCulture, out double dPoints))
                        {
                            points = (int)Math.Round(dPoints);
                        }
                        
                        Console.WriteLine($"[Leaderboard] Parsed: {name} = {points} (Raw: '{row[1]}')");
                    }
                }

                target.Add(new LeaderboardItem { Name = name, Points = points, Rank = rank++ });
            }

            // Sort by Points descending, re-assign rank
            var sortedItems = target.OrderByDescending(x => x.Points).ToList();
            target.Clear();
            rank = 1;
            foreach (var item in sortedItems)
            {
                item.Rank = rank++;
                target.Add(item);
            }
        }
    }
}

public class LeaderboardItem
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public string AvatarUrl { get; set; } = "";
    
    // Display with Indonesian thousand separator: 10713 → "10.713"
    public string FormattedPoints => Points.ToString("N0", new System.Globalization.CultureInfo("id-ID"));
}
