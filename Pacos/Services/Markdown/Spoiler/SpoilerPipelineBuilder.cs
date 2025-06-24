using Markdig;

namespace Pacos.Services.Markdown.Spoiler;

public static class SpoilerPipelineBuilder
{
    public static MarkdownPipelineBuilder UseSpoilers(this MarkdownPipelineBuilder pipeline)
    {
        var extensions = pipeline.Extensions;

        if (!extensions.Contains<SpoilerExtension>())
        {
            extensions.Add(new SpoilerExtension());
        }

        return pipeline;
    }
}
