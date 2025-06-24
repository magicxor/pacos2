using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Pacos.Services.Markdown.Spoiler;

public sealed class SpoilerInlineParser : InlineParser
{
    public SpoilerInlineParser()
    {
        OpeningCharacters = ['|'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Allow preceding whitespace
        var match = slice.CurrentChar;
        if (slice.PeekCharExtra(-1) == match)
        {
            return false;
        }

        var startPosition = slice.Start;

        // Match open pipes
        var openPipes = CountAndSkipChar(ref slice, match);

        // Require precisely two of them
        if (openPipes != 2)
        {
            return false;
        }

        var closePipes = 0;
        var c = slice.CurrentChar;
        var builder = StringBuilderCache.Local();
        var allSpace = true;

        while (c != '\0')
        {
            if (c == match)
            {
                closePipes = CountAndSkipChar(ref slice, match);

                if (openPipes == closePipes)
                {
                    break;
                }

                allSpace = false;
                builder.Append(match, closePipes);
                c = slice.CurrentChar;
            }
            else
            {
                builder.Append(c);
                if (c != ' ')
                {
                    allSpace = false;
                }

                c = slice.NextChar();
            }
        }

        // Stop parsing if opening and closing pipes amount is different
        if (closePipes != openPipes)
        {
            return false;
        }

        // Set content
        string content;
        if (!allSpace && builder.Length > 2 && builder[0] == ' ' && builder[^1] == ' ')
        {
            content = builder.ToString(1, builder.Length - 2);
        }
        else
        {
            content = builder.ToString();
        }

        // Create the tag
        var spoiler = new SpoilerInline
        {
            Span = new SourceSpan(processor.GetSourcePosition(startPosition, out var line, out var column), processor.GetSourcePosition(slice.Start - 1)),
            Line = line,
            Column = column,
            Content = new StringSlice(content),
        };
        spoiler.GetAttributes().AddClass("spoiler");
        processor.Inline = spoiler;

        return true;
    }

    private static int CountAndSkipChar(ref StringSlice slice, char matchChar)
    {
        var text = slice.Text;
        var end = slice.End;
        var current = slice.Start;
        while (current <= end && (uint)current < (uint)text.Length && text[current] == matchChar)
        {
            current++;
        }
        var count = current - slice.Start;
        slice.Start = current;

        return count;
    }
}
