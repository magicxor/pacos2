using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Pacos.Services.Markdown.Spoiler;

namespace Pacos.Services.Markdown;

public sealed class TelegramMarkdownRenderer
{
    private static readonly FrozenSet<char> SpecialChars = new HashSet<char> { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' }.ToFrozenSet();

    private readonly StringBuilder _output = new();

    public string Render(MarkdownDocument document)
    {
        _output.Clear();
        foreach (var block in document)
        {
            RenderBlock(block);
        }
        return _output.ToString().Trim();
    }

    private void RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph);
                break;
            case ListBlock list:
                RenderList(list);
                break;
            case QuoteBlock quote:
                RenderQuote(quote);
                break;
            case CodeBlock code:
                RenderCodeBlock(code);
                break;
            case Table table:
                RenderTable(table);
                break;
            case ThematicBreakBlock:
                _output.AppendLine("\n\\-\\-\\-\n");
                break;
            case HtmlBlock html:
                RenderHtmlBlock(html);
                break;
            default:
                // For other block types, try to extract and render any inline content
                if (block is ContainerBlock container)
                {
                    foreach (var child in container)
                    {
                        RenderBlock(child);
                    }
                }
                break;
        }
    }

    private void RenderHeading(HeadingBlock heading)
    {
        // Telegram doesn't support headers, so we'll make them bold
        _output.Append('*');
        if (heading.Inline != null)
        {
            foreach (var inline in heading.Inline)
            {
                RenderInline(inline, true);
            }
        }
        _output.AppendLine("*\n");
    }

    private void RenderParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                RenderInline(inline);
            }
        }
        _output.AppendLine("\n");
    }

    private void RenderList(ListBlock list)
    {
        int index = 1;
        foreach (var listItemBlock in list)
        {
            var item = (ListItemBlock)listItemBlock;
            // Check if this is a task list item
            bool isTaskList = false;
            string checkboxText = string.Empty;
            string remainingText = string.Empty;

            if (!list.IsOrdered && item.Count > 0 && item[0] is ParagraphBlock firstPara && firstPara.Inline != null)
            {
                // Check if the first inline element is a TaskList
                var firstInline = firstPara.Inline.FirstChild;
                if (firstInline != null && firstInline.GetType().Name == "TaskList")
                {
                    isTaskList = true;

                    // Get the task list state using reflection
                    var checkedProperty = firstInline.GetType().GetProperty("Checked");
                    bool isChecked = checkedProperty != null && checkedProperty.GetValue(firstInline) is bool checkedValue && checkedValue;

                    checkboxText = isChecked ? @"\[x\] " : @"\[ \] ";

                    // Get the remaining text from the second inline element (LiteralInline)
                    var secondInline = firstInline.NextSibling;
                    if (secondInline is LiteralInline literal)
                    {
                        remainingText = literal.Content.ToString();
                    }
                }
            }

            if (list.IsOrdered)
            {
                _output.Append(CultureInfo.InvariantCulture, $"{index}\\. ");
                index++;
            }
            else if (isTaskList)
            {
                _output.Append(CultureInfo.InvariantCulture, $"\\- {checkboxText}");
            }
            else
            {
                _output.Append("• ");
            }

            if (isTaskList)
            {
                // For task lists, just output the remaining text after checkbox
                _output.Append(EscapeText(remainingText));
            }
            else
            {
                // For regular lists, render all blocks normally
                foreach (var block in item)
                {
                    if (block is ParagraphBlock para)
                    {
                        if (para.Inline != null)
                        {
                            foreach (var inline in para.Inline)
                            {
                                RenderInline(inline);
                            }
                        }
                    }
                    else if (block is ListBlock nestedList)
                    {
                        // Add a line break before nested lists but no extra line
                        _output.AppendLine();
                        var nestedRenderer = new TelegramMarkdownRenderer();
                        string nestedContent = nestedRenderer.RenderListDirectly(nestedList, "  ");
                        // Remove the trailing newline from nested content to avoid double spacing
                        _output.Append(nestedContent.TrimEnd());
                    }
                    else
                    {
                        RenderBlock(block);
                    }
                }
            }
            _output.AppendLine();
        }
        _output.AppendLine();
    }

    private string RenderListDirectly(ListBlock list, string indent)
    {
        var nestedOutput = new StringBuilder();
        int index = 1;
        foreach (var listItemBlock in list)
        {
            var item = (ListItemBlock)listItemBlock;
            // Check if this is a task list item
            bool isTaskList = false;
            string checkboxText = string.Empty;
            string remainingText = string.Empty;

            if (!list.IsOrdered && item.Count > 0 && item[0] is ParagraphBlock firstPara && firstPara.Inline != null)
            {
                // Check if the first inline element is a TaskList
                var firstInline = firstPara.Inline.FirstChild;
                if (firstInline != null && firstInline.GetType().Name == "TaskList")
                {
                    isTaskList = true;

                    // Get the task list state using reflection
                    var checkedProperty = firstInline.GetType().GetProperty("Checked");
                    bool isChecked = checkedProperty != null && checkedProperty.GetValue(firstInline) is bool checkedValue && checkedValue;

                    checkboxText = isChecked ? @"\[x\] " : @"\[ \] ";

                    // Get the remaining text from the second inline element (LiteralInline)
                    var secondInline = firstInline.NextSibling;
                    if (secondInline is LiteralInline literal)
                    {
                        remainingText = literal.Content.ToString();
                    }
                }
            }

            if (list.IsOrdered)
            {
                nestedOutput.Append(CultureInfo.InvariantCulture, $"{indent}{index}\\. ");
                index++;
            }
            else if (isTaskList)
            {
                nestedOutput.Append(CultureInfo.InvariantCulture, $"{indent}\\- {checkboxText}");
            }
            else
            {
                nestedOutput.Append(CultureInfo.InvariantCulture, $"{indent}• ");
            }

            if (isTaskList)
            {
                // For task lists, just output the remaining text after checkbox
                nestedOutput.Append(EscapeText(remainingText));
            }
            else
            {
                // For regular lists, render all blocks normally
                foreach (var block in item)
                {
                    if (block is ParagraphBlock para)
                    {
                        if (para.Inline != null)
                        {
                            foreach (var inline in para.Inline)
                            {
                                var inlineRenderer = new TelegramMarkdownRenderer();
                                inlineRenderer.RenderInline(inline);
                                nestedOutput.Append(inlineRenderer._output);
                            }
                        }
                    }
                    else if (block is ListBlock nestedList)
                    {
                        nestedOutput.AppendLine();
                        string nestedListContent = RenderListDirectly(nestedList, indent + "  ");
                        nestedOutput.Append(nestedListContent.TrimEnd());
                    }
                }
            }
            nestedOutput.AppendLine();
        }
        return nestedOutput.ToString();
    }

    private void RenderQuote(QuoteBlock quote)
    {
        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                // Collect all the text from this paragraph first
                var paraRenderer = new TelegramMarkdownRenderer();
                if (para.Inline != null)
                {
                    foreach (var inline in para.Inline)
                    {
                        paraRenderer.RenderInline(inline);
                    }
                }

                // Split the content by lines and add > prefix to each
                string paraContent = paraRenderer._output.ToString().Trim();
                string[] lines = paraContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    _output.AppendLine(">" + line);
                }
            }
            else
            {
                // For non-paragraph blocks, render with quote prefix
                var blockRenderer = new TelegramMarkdownRenderer();
                blockRenderer.RenderBlock(block);
                string blockContent = blockRenderer._output.ToString();
                string[] lines = blockContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    _output.AppendLine(">" + line);
                }
            }
        }
        _output.AppendLine();
    }

    private void RenderCodeBlock(CodeBlock code)
    {
        if (code is FencedCodeBlock fenced && !string.IsNullOrEmpty(fenced.Info))
        {
            _output.AppendLine(CultureInfo.InvariantCulture, $"```{EscapeCodeContent(fenced.Info)}");
        }
        else
        {
            _output.AppendLine("```");
        }

        foreach (var line in code.Lines)
        {
            _output.AppendLine(EscapeCodeContent(line.ToString() ?? string.Empty));
        }
        _output.AppendLine("```\n");
    }

    private void RenderTable(Table table)
    {
        // Telegram doesn't support tables, so we'll render as preformatted text
        _output.AppendLine("```");

        foreach (var row in table)
        {
            if (row is TableRow tableRow)
            {
                var cells = new List<string>();
                foreach (var cell in tableRow)
                {
                    if (cell is TableCell tableCell)
                    {
                        var cellContent = new StringBuilder();
                        foreach (var block in tableCell)
                        {
                            if (block is ParagraphBlock { Inline: not null } para)
                            {
                                foreach (var inline in para.Inline)
                                {
                                    cellContent.Append(GetPlainText(inline));
                                }
                            }
                        }
                        cells.Add(cellContent.ToString());
                    }
                }
                _output.AppendLine(EscapeCodeContent(string.Join(" | ", cells)));
            }
        }

        _output.AppendLine("```\n");
    }

    private void RenderHtmlBlock(HtmlBlock html)
    {
        // Convert simple HTML tags to Telegram markdown
        string content = html.Lines.ToString() ?? string.Empty;
        content = Regex.Replace(content, "<b>(.*?)</b>", "*$1*", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<i>(.*?)</i>", "_$1_", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<u>(.*?)</u>", "__$1__", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<s>(.*?)</s>", "~$1~", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<code>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, "<[^>]+>", string.Empty, RegexOptions.IgnoreCase); // Remove other HTML tags

        _output.AppendLine(EscapeText(content) + "\n");
    }

    private void RenderInline(Inline inline, bool insideFormatting = false)
    {
        switch (inline)
        {
            case LiteralInline literal:
                _output.Append(EscapeText(literal.Content.ToString()));
                break;
            case EmphasisInline emphasis:
                RenderEmphasis(emphasis);
                break;
            case LinkInline link:
                RenderLink(link);
                break;
            case CodeInline code:
                _output.Append(CultureInfo.InvariantCulture, $"`{EscapeCodeContent(code.Content)}`");
                break;
            case LineBreakInline:
                _output.AppendLine();
                break;
            case HtmlInline html:
                RenderHtmlInline(html);
                break;
            case AutolinkInline autolink:
                _output.Append(CultureInfo.InvariantCulture, $"[{EscapeText(autolink.Url)}]({EscapeLinkUrl(autolink.Url)})");
                break;
            case SpoilerInline spoiler:
                _output.Append("||");
                _output.Append(EscapeText(spoiler.Content.ToString()));
                _output.Append("||");
                break;
            default:
                // For unknown inline types, check if it's a container
                if (inline is ContainerInline container)
                {
                    foreach (var child in container)
                    {
                        RenderInline(child, insideFormatting);
                    }
                }
                break;
        }
    }

    private void RenderEmphasis(EmphasisInline emphasis)
    {
        string marker = string.Empty;

        // Handle different emphasis types based on delimiter character and count
        if (emphasis.DelimiterChar == '_' && emphasis.DelimiterCount == 2)
        {
            // Double underscore should be underline in Telegram
            marker = "__";
        }
        else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 2)
        {
            // Double asterisk is bold
            marker = "*";
        }
        else if (emphasis.DelimiterChar == '_' && emphasis.DelimiterCount == 1)
        {
            // Single underscore is italic
            // zero-width space is used to avoid conflicts
            marker = "\u200B_\u200B";
        }
        else if (emphasis.DelimiterChar == '*' && emphasis.DelimiterCount == 1)
        {
            // Single asterisk is italic (but we prefer underscore for consistency)
            // zero-width space is used to avoid conflicts
            marker = "\u200B_\u200B";
        }
        else if (emphasis.DelimiterChar == '~')
        {
            // Strikethrough
            marker = "~";
        }

        if (!string.IsNullOrEmpty(marker))
        {
            _output.Append(marker);
            foreach (var child in emphasis)
            {
                RenderInline(child, true);
            }
            _output.Append(marker);
        }
    }

    private void RenderLink(LinkInline link)
    {
        if (link.IsImage)
        {
            // Images are not supported in Telegram markdown, show as a link instead
            _output.Append('[');
            // Use alt text from the image, or "Image" as fallback
            foreach (var child in link)
            {
                RenderInline(child, true);
            }
            // If no alt text was found, use a default
            if (link.FirstChild == null)
            {
                _output.Append("Image");
            }
            _output.Append(CultureInfo.InvariantCulture, $"]({EscapeLinkUrl(link.Url ?? string.Empty)})");
        }
        else
        {
            _output.Append('[');
            foreach (var child in link)
            {
                RenderInline(child, true);
            }
            _output.Append(CultureInfo.InvariantCulture, $"]({EscapeLinkUrl(link.Url ?? string.Empty)})");
        }
    }

    private void RenderHtmlInline(HtmlInline html)
    {
        string tag = html.Tag;
        switch (tag.ToLowerInvariant())
        {
            case "b":
            case "strong":
                _output.Append('*');
                break;
            case "/b":
            case "/strong":
                _output.Append('*');
                break;
            case "i":
            case "em":
                _output.Append("\u200B_\u200B");
                break;
            case "/i":
            case "/em":
                _output.Append("\u200B_\u200B");
                break;
            case "u":
                _output.Append("__");
                break;
            case "/u":
                _output.Append("__");
                break;
            case "s":
            case "strike":
                _output.Append('~');
                break;
            case "/s":
            case "/strike":
                _output.Append('~');
                break;
            case "code":
                _output.Append('`');
                break;
            case "/code":
                _output.Append('`');
                break;
            default:
                // Ignore other HTML tags
                break;
        }
    }

    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = new StringBuilder();
        foreach (char c in text)
        {
            if (SpecialChars.Contains(c) || c == '\\')
            {
                result.Append('\\');
            }
            result.Append(c);
        }
        return result.ToString();
    }

    private static string EscapeCodeContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Replace("\\", @"\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string EscapeLinkUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        return url.Replace("\\", @"\\", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    }

    private string GetPlainText(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return literal.Content.ToString();
            case EmphasisInline emphasis:
                var result = new StringBuilder();
                foreach (var child in emphasis)
                {
                    result.Append(GetPlainText(child));
                }
                return result.ToString();
            case LinkInline link:
                var linkResult = new StringBuilder();
                foreach (var child in link)
                {
                    linkResult.Append(GetPlainText(child));
                }
                return linkResult.ToString();
            case CodeInline code:
                return code.Content;
            default:
                return string.Empty;
        }
    }
}
