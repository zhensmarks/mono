using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
        var mainTextBlock = this.FindControl<SelectableTextBlock>("MainTextBlock");
        if (mainTextBlock == null) return;
        
        mainTextBlock.Inlines!.Clear();

        if (string.IsNullOrEmpty(text)) return;

        // Simple Parser for Bold (**text**) and Italic (*text* or _text_)
        // Regex to split by bold/italic tokens
        // Matches:
        // 1. **bold**
        // 2. *italic*
        // 3. _italic_
        // 4. `code`
        // 5. ~~strike~~
        
        string pattern = @"(\*\*.*?\*\*)|(\*.*?\*)|(_.*?_)|(`.*?`)|(~~.*?~~)";
        
        var matches = Regex.Matches(text, pattern);
        
        int lastIndex = 0;
        
        foreach (Match match in matches)
        {
            // Add preceding format-less text
            if (match.Index > lastIndex)
            {
                mainTextBlock.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
            }

            string content = match.Value;

            if (content.StartsWith("**") && content.EndsWith("**"))
            {
                mainTextBlock.Inlines.Add(new Run 
                { 
                    Text = content.Substring(2, content.Length - 4), 
                    FontWeight = FontWeight.Bold 
                });
            }
            else if ((content.StartsWith("*") && content.EndsWith("*")) || (content.StartsWith("_") && content.EndsWith("_")))
            {
                mainTextBlock.Inlines.Add(new Run 
                { 
                    Text = content.Substring(1, content.Length - 2), 
                    FontStyle = FontStyle.Italic 
                });
            }
            else if (content.StartsWith("`") && content.EndsWith("`"))
            {
                mainTextBlock.Inlines.Add(new Run 
                { 
                    Text = content.Substring(1, content.Length - 2), 
                    FontFamily = "Consolas, Monospace",
                    Background = SolidColorBrush.Parse("#20FFFFFF")
                });
            }
            else if (content.StartsWith("~~") && content.EndsWith("~~"))
            {
                 mainTextBlock.Inlines.Add(new Run 
                { 
                    Text = content.Substring(2, content.Length - 4), 
                    TextDecorations = TextDecorations.Strikethrough
                });
            }
            
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
        {
            mainTextBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
        }
    }
}
