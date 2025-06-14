using Markdig;
using Pacos.Extensions;
using Pacos.Services;
using Pacos.Services.Markdown;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class TelegramMarkdownRendererTests
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseMdExtensions()
        .Build();

    private static readonly VerifySettings VerifySettings = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // VerifySettings.DisableDiff();
    }

    [Test]
    public void Render_WhenDocumentIsEmpty_ShouldReturnEmptyString()
    {
        const string standardMarkdown = "";
        const string expectedTelegramMarkdown = "";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public void Render_WhenDocumentContainsText_ShouldReturnText()
    {
        const string standardMarkdown = "Hello World";
        const string expectedTelegramMarkdown = "Hello World";

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);
        Assert.That(actualTelegramMarkdown, Is.EqualTo(expectedTelegramMarkdown));
    }

    [Test]
    public async Task Render_WhenHasCheckboxes_ShouldReturnValidMarkdown()
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", "checkbox_test.md"));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings);
    }

    [Test]
    public async Task Render_WhenHasImages_ShouldReturnValidMarkdown()
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", "image_test.md"));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings);
    }

    [Test]
    public async Task Render_WhenHasTables_ShouldReturnValidMarkdown()
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", "table_test.md"));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings);
    }

    [Test]
    public async Task Render_WhenHasComplexMarkdownEn_ShouldReturnValidMarkdown()
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", "test_all_en.md"));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings);
    }

    [Test]
    public async Task Render_WhenHasComplexMarkdownRu_ShouldReturnValidMarkdown()
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", "test_all_ru.md"));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings);
    }
}
