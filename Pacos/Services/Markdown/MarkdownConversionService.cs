using Markdig;
using Pacos.Extensions;

namespace Pacos.Services.Markdown;

public sealed class MarkdownConversionService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseMdExtensions()
        .Build();
    private readonly ILogger<MarkdownConversionService> _logger;

    public MarkdownConversionService(
        ILogger<MarkdownConversionService> logger)
    {
        _logger = logger;
    }

    public string ConvertToTelegramMarkdown(string normalMarkdown)
    {
        _logger.LogDebug("Converting normal markdown to Telegram markdown: {NormalMarkdown}", normalMarkdown);
        var document = Markdig.Markdown.Parse(normalMarkdown, MarkdownPipeline);
        return new TelegramMarkdownRenderer().Render(document);
    }
}
