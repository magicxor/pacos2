using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Pacos.Services.Markdown.Spoiler;

namespace Pacos.Extensions;

public static class MarkdownPipelineExtensions
{
    public static MarkdownPipelineBuilder UseMdExtensions(this MarkdownPipelineBuilder pipeline)
    {
        return pipeline
            .UseSpoilers()
            .UseAlertBlocks()
            .UseAutoIdentifiers()
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseGridTables()
            .UseMediaLinks()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseAutoLinks()
            .UseGenericAttributes(); // Must be last as it is one parser that is modifying other parsers
    }
}
