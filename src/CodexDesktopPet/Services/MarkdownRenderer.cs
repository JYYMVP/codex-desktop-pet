using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using MediaBrush = System.Windows.Media.Brush;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace CodexDesktopPet.Services;

internal static partial class MarkdownRenderer
{
    private static readonly MediaBrush AccentBrush = FreezeBrush("#68E9FF");
    private static readonly MediaBrush MutedBrush = FreezeBrush("#A6A6AD");
    private static readonly MediaBrush CodeForegroundBrush = FreezeBrush("#D7E3FF");
    private static readonly MediaBrush CodeBackgroundBrush = FreezeBrush("#2A2A2D");
    private static readonly MediaFontFamily CodeFont = new("Cascadia Mono, Consolas");

    public static void Render(TextBlock target, string markdown)
    {
        target.Inlines.Clear();
        var lines = (markdown ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var inCodeBlock = false;
        var codeLanguage = string.Empty;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = line.Trim()[3..].Trim();
                    if (!string.IsNullOrWhiteSpace(codeLanguage))
                        AddCodeRun(target, codeLanguage, isLanguageLabel: true);
                }
                else
                {
                    inCodeBlock = false;
                    codeLanguage = string.Empty;
                }
            }
            else if (inCodeBlock)
            {
                AddCodeRun(target, line, isLanguageLabel: false);
            }
            else
            {
                AddMarkdownLine(target, line);
            }

            if (index < lines.Length - 1)
                target.Inlines.Add(new LineBreak());
        }

        if (inCodeBlock && !string.IsNullOrWhiteSpace(codeLanguage))
            target.Inlines.Add(new LineBreak());
    }

    private static void AddMarkdownLine(TextBlock target, string line)
    {
        var content = line;
        var fontSize = target.FontSize;
        var fontWeight = FontWeights.Normal;
        var prefix = string.Empty;
        MediaBrush? prefixBrush = null;
        var italicLine = false;

        var heading = HeadingRegex().Match(content);
        if (heading.Success)
        {
            var level = heading.Groups[1].Value.Length;
            content = heading.Groups[2].Value;
            fontSize = level switch { 1 => 18, 2 => 16, _ => 15 };
            fontWeight = FontWeights.SemiBold;
        }
        else if (UnorderedListRegex().IsMatch(content))
        {
            content = UnorderedListRegex().Replace(content, string.Empty, 1);
            prefix = "•  ";
            prefixBrush = AccentBrush;
        }
        else
        {
            var ordered = OrderedListRegex().Match(content);
            if (ordered.Success)
            {
                prefix = ordered.Groups[1].Value + "  ";
                prefixBrush = AccentBrush;
                content = ordered.Groups[2].Value;
            }
            else if (content.TrimStart().StartsWith('>'))
            {
                content = content.TrimStart()[1..].TrimStart();
                prefix = "▍ ";
                prefixBrush = AccentBrush;
                italicLine = true;
            }
        }

        if (!string.IsNullOrEmpty(prefix))
            target.Inlines.Add(new Run(prefix) { Foreground = prefixBrush });

        var span = new Span { FontSize = fontSize, FontWeight = fontWeight };
        if (italicLine)
        {
            span.FontStyle = FontStyles.Italic;
            span.Foreground = MutedBrush;
        }
        AddInlineMarkdown(span.Inlines, content);
        target.Inlines.Add(span);
    }

    private static void AddInlineMarkdown(InlineCollection inlines, string text)
    {
        var cursor = 0;
        foreach (Match match in InlineTokenRegex().Matches(text))
        {
            if (match.Index > cursor)
                inlines.Add(new Run(text[cursor..match.Index]));

            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) ||
                token.StartsWith("__", StringComparison.Ordinal))
            {
                inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith('`'))
            {
                inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = CodeFont,
                    Foreground = CodeForegroundBrush,
                    Background = CodeBackgroundBrush
                });
            }
            else if (token.StartsWith('['))
            {
                var linkMatch = LinkRegex().Match(token);
                if (linkMatch.Success && Uri.TryCreate(linkMatch.Groups[2].Value,
                        UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink(new Run(linkMatch.Groups[1].Value))
                    {
                        NavigateUri = uri,
                        Foreground = AccentBrush
                    };
                    link.RequestNavigate += OpenLink;
                    inlines.Add(link);
                }
                else
                {
                    inlines.Add(new Run(token));
                }
            }
            else
            {
                inlines.Add(new Italic(new Run(token[1..^1])));
            }
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            inlines.Add(new Run(text[cursor..]));
    }

    private static void AddCodeRun(TextBlock target, string text, bool isLanguageLabel)
    {
        target.Inlines.Add(new Run(isLanguageLabel ? text.ToUpperInvariant() : $" {text} ")
        {
            FontFamily = CodeFont,
            FontSize = isLanguageLabel ? 10 : 12.5,
            FontWeight = isLanguageLabel ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isLanguageLabel ? MutedBrush : CodeForegroundBrush,
            Background = CodeBackgroundBrush
        });
    }

    private static void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch
        {
            // A malformed or blocked link should not break the answer bubble.
        }
    }

    private static SolidColorBrush FreezeBrush(string value)
    {
        var brush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }

    [GeneratedRegex("^(#{1,3})\\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("^\\s*[-*+]\\s+")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex("^\\s*((?:\\d+)[.)])\\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex("(\\*\\*[^*\\n]+\\*\\*|__[^_\\n]+__|`[^`\\n]+`|\\[[^]\\n]+\\]\\(https?://[^)\\s]+\\)|(?<!\\*)\\*[^*\\n]+\\*(?!\\*)|(?<!_)_[^_\\n]+_(?!_))")]
    private static partial Regex InlineTokenRegex();

    [GeneratedRegex("^\\[([^]]+)\\]\\((https?://[^)]+)\\)$")]
    private static partial Regex LinkRegex();
}
