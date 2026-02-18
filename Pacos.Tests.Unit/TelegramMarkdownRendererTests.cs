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
        VerifySettings.DisableDiff();
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
    [TestCase("checkbox_test.md")]
    [TestCase("image_test.md")]
    [TestCase("table_test.md")]
    [TestCase("test_all_en.md")]
    [TestCase("test_all_ru.md")]
    [TestCase("quote_bug.md")]
    [TestCase("complex_list.md")]
    [TestCase("nested_list_blocks.md")]
    [TestCase("task_list_formatting.md")]
    [TestCase("nested_lists.md")]
    [TestCase("code_blocks_list.md")]
    [TestCase("code_blocks.md")]
    public async Task Render_ShouldReturnValidMarkdown(string fileName)
    {
        var standardMarkdown = await File.ReadAllTextAsync(Path.Combine("Files", fileName));

        var standardMarkdownDoc = Markdown.Parse(standardMarkdown, MarkdownPipeline);
        var actualTelegramMarkdown = new TelegramMarkdownRenderer().Render(standardMarkdownDoc);

        await Verify(actualTelegramMarkdown, VerifySettings).UseParameters(fileName);
    }
}
