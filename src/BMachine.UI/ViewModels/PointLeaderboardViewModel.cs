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
    private async Task LoadData()
    {
        if (_database == null) return;
        IsLoading = true;
        Items.Clear();

        try
        {
            var range = await _database.GetAsync<string>("Leaderboard.Range");
            var sheetId = await _database.GetAsync<string>("Google.SheetId");
            var sheetName = await _database.GetAsync<string>("Google.SheetName");
            var credsPath = await _database.GetAsync<string>("Google.CredsPath");

            if (string.IsNullOrEmpty(range) || string.IsNullOrEmpty(sheetId) || string.IsNullOrEmpty(credsPath))
            {
                // Items.Add(new LeaderboardItem { Name = "Config Missing", Points = 0 }); // Optional: Show error
                IsLoading = false;
                return;
            }

            // Simple Google Sheet Logic (Replicated to avoid Dependency Injection complexity for now)
            // Ideally should use a shared Service.
            
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
                             // If 2nd column starts with letter or contains "TOTAL"/"POIN", it's likely a header
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
                     if (row.Count >= 2)
                     {
                         var pStr = row[1]?.ToString()?.Trim();
                         if (!string.IsNullOrEmpty(pStr))
                         {
                             // Try parsing as double first (handles decimals from formulas)
                             if (double.TryParse(pStr, System.Globalization.NumberStyles.Any, 
                                                System.Globalization.CultureInfo.InvariantCulture, out double dPoints))
                             {
                                 points = (int)Math.Round(dPoints);
                             }
                             else
                             {
                                 // Fallback: remove thousand separators and try again
                                 pStr = pStr.Replace(",", "").Replace(" ", "");
                                 if (double.TryParse(pStr, System.Globalization.NumberStyles.Any,
                                                    System.Globalization.CultureInfo.InvariantCulture, out dPoints))
                                 {
                                     points = (int)Math.Round(dPoints);
                                 }
                             }
                             
                             Console.WriteLine($"[Leaderboard] Parsed: {name} = {points} (Raw: '{row[1]}')");
                         }
                     }

                     Items.Add(new LeaderboardItem { Name = name, Points = points, Rank = rank++ });
                 }
                 
                 // Sort by Points descending (terbesar ke terkecil), kemudian re-assign rank
                 var sortedItems = Items.OrderByDescending(x => x.Points).ToList();
                 Items.Clear();
                 rank = 1;
                 foreach (var item in sortedItems)
                 {
                     item.Rank = rank++;
                     Items.Add(item);
                 }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Leaderboard Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class LeaderboardItem
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public string AvatarUrl { get; set; } = "";
}
