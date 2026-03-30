using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Interactivity;
using BMachine.UI.Views;

namespace BMachine.UI.Controls;

public partial class FormattedTextBlock : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<FormattedTextBlock, string>(nameof(Text));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FormattedTextBlock()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
             OnTextChanged(change.NewValue as string ?? "");
        }
    }

    private void OnTextChanged(string text)
    {
        var rootPanel = this.FindControl<StackPanel>("RootPanel");
        if (rootPanel == null) return;
        
        rootPanel.Children.Clear();

        if (string.IsNullOrEmpty(text)) return;

        // Regex to find Markdown images: ![alt](url) OR plain image URLs
        string imagePattern = @"(!\[.*?\]\((https?://[^\s]+)\))|((https?://[^\s]+\.(?:png|jpg|jpeg|gif|webp)(?:\?[^\s]*)?))";
        
        var matches = Regex.Matches(text, imagePattern, RegexOptions.IgnoreCase);
        
        int lastIndex = 0;
        
        foreach (Match match in matches)
        {
            // Add preceding text
            if (match.Index > lastIndex)
            {
                var textPart = text.Substring(lastIndex, match.Index - lastIndex).Trim('\n', '\r');
                if (!string.IsNullOrWhiteSpace(textPart))
                {
                    rootPanel.Children.Add(CreateTextBlock(textPart));
                }
            }

            // Extract URL
            string imageUrl = "";
            if (match.Groups[1].Success) // Markdown image
            {
                imageUrl = match.Groups[2].Value;
            }
            else if (match.Groups[3].Success) // Plain URL
            {
                imageUrl = match.Groups[3].Value;
            }

            if (!string.IsNullOrEmpty(imageUrl))
            {
                rootPanel.Children.Add(CreateImageControl(imageUrl));
            }
            
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
        {
            var textPart = text.Substring(lastIndex).Trim('\n', '\r');
            if (!string.IsNullOrWhiteSpace(textPart))
            {
                rootPanel.Children.Add(CreateTextBlock(textPart));
            }
        }
    }

    private SelectableTextBlock CreateTextBlock(string text)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Background = Brushes.Transparent,
            Focusable = true,
            Foreground = this.Foreground,
            FontSize = this.FontSize,
            SelectionBrush = (IBrush?)this.FindResource("AccentColorBrush"),
            SelectionForegroundBrush = null
        };

        tb.KeyDown += OnKeyDown;

        // Apply basic bold/italic formatting
        string pattern = @"(\*\*.*?\*\*)|(\*.*?\*)|(_.*?_)|(`.*?`)|(~~.*?~~)";
        var matches = Regex.Matches(text, pattern);
        
        if (matches.Count == 0)
        {
            tb.Text = text;
            return tb;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
                tb.Inlines!.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));

            string content = match.Value;

            if (content.StartsWith("**") && content.EndsWith("**"))
                tb.Inlines!.Add(new Run { Text = content.Substring(2, content.Length - 4), FontWeight = FontWeight.Bold });
            else if ((content.StartsWith("*") && content.EndsWith("*")) || (content.StartsWith("_") && content.EndsWith("_")))
                tb.Inlines!.Add(new Run { Text = content.Substring(1, content.Length - 2), FontStyle = FontStyle.Italic });
            else if (content.StartsWith("`") && content.EndsWith("`"))
                tb.Inlines!.Add(new Run { Text = content.Substring(1, content.Length - 2), FontFamily = "Consolas, Monospace", Background = SolidColorBrush.Parse("#20FFFFFF") });
            else if (content.StartsWith("~~") && content.EndsWith("~~"))
                tb.Inlines!.Add(new Run { Text = content.Substring(2, content.Length - 4), TextDecorations = TextDecorations.Strikethrough });
            
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            tb.Inlines!.Add(new Run(text.Substring(lastIndex)));

        return tb;
    }

    private Control CreateImageControl(string url)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Margin = new Thickness(0, 5, 0, 5),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            MaxHeight = 300,
            MaxWidth = 400,
            Background = SolidColorBrush.Parse("#1AFFFFFF")
        };

        var img = new Image 
        { 
            Stretch = Avalonia.Media.Stretch.Uniform,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var btn = new Button
        {
            Content = img,
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        
        btn.Click += (s, e) =>
        {
            var lightbox = new ImageLightboxWindow(url);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window win)
            {
                lightbox.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner;
                lightbox.Show(win);
            }
            else
            {
                lightbox.Show();
            }
        };

        border.Child = btn;

        // Load image asynchronously
        _ = DownloadAndSetImageAsync(url, img);

        return border;
    }

    private async System.Threading.Tasks.Task DownloadAndSetImageAsync(string url, Image imgControl)
    {
        try
        {
            var cleanUrl = url;
            string apiKey = "";
            string token = "";

            var matchKey = Regex.Match(url, @"[?&]key=([^&]+)");
            var matchToken = Regex.Match(url, @"[?&]token=([^&]+)");

            if (matchKey.Success && matchToken.Success)
            {
                apiKey = matchKey.Groups[1].Value;
                token = matchToken.Groups[1].Value;
            }

            using var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true };
            using var client = new System.Net.Http.HttpClient(handler);
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(token) && url.Contains("trello.com"))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"OAuth oauth_consumer_key=\"{apiKey}\", oauth_token=\"{token}\"");
            }

            var response = await client.GetAsync(cleanUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                System.Console.WriteLine($"[FormattedTextBlock] Failed downloading inline image: {response.StatusCode} for URL: {cleanUrl}");
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                using var stream = new System.IO.MemoryStream(bytes);
                try 
                { 
                    stream.Position = 0;
                    imgControl.Source = new Avalonia.Media.Imaging.Bitmap(stream); 
                } 
                catch(System.Exception ex) 
                {
                     System.Console.WriteLine($"[FormattedTextBlock] Error loading bitmap: {ex.Message}");
                }
            });
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[FormattedTextBlock] Exception {ex.Message} on URL: {url}");
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            if (sender is SelectableTextBlock tb && !string.IsNullOrEmpty(tb.SelectedText))
            {
                 var topLevel = TopLevel.GetTopLevel(this);
                 if (topLevel?.Clipboard != null)
                 {
                     await topLevel.Clipboard.SetTextAsync(tb.SelectedText);
                 }
            }
        }
    }
}
