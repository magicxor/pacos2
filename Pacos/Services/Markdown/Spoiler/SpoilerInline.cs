using System.Diagnostics;
using Markdig.Helpers;
using Markdig.Syntax.Inlines;

namespace Pacos.Services.Markdown.Spoiler;

[DebuggerDisplay("{" + nameof(Content) + "}")]
public sealed class SpoilerInline : LeafInline
{
    public StringSlice Content { get; init; }
}
