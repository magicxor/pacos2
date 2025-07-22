using Pacos.Services;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class WordFilterTests
{
    private readonly string[] _testBannedWords = ["spam", "badword", "inappropriate"];
    private WordFilter _wordFilter = new([]);

    [SetUp]
    public void SetUp()
    {
        _wordFilter = new WordFilter(_testBannedWords);
    }

    [Test]
    public void Constructor_WhenBannedWordsProvided_ShouldCreateInstance()
    {
        var bannedWords = new[] { "test", "word" };
        var wordFilter = new WordFilter(bannedWords);

        Assert.That(wordFilter, Is.Not.Null);
    }

    [Test]
    public void Constructor_WhenEmptyArray_ShouldCreateInstance()
    {
        var wordFilter = new WordFilter([]);

        Assert.That(wordFilter, Is.Not.Null);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsNull_ShouldReturnFalse()
    {
        const string? message = null;
        var result = _wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsEmpty_ShouldReturnFalse()
    {
        var result = _wordFilter.ContainsBannedWords(string.Empty);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsWhitespace_ShouldReturnFalse()
    {
        var result = _wordFilter.ContainsBannedWords("   ");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsTab_ShouldReturnFalse()
    {
        var result = _wordFilter.ContainsBannedWords("\t");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsNewline_ShouldReturnFalse()
    {
        var result = _wordFilter.ContainsBannedWords("\n");

        Assert.That(result, Is.False);
    }

    [TestCase("This is a clean message")]
    [TestCase("Hello world")]
    [TestCase("No banned content here")]
    [TestCase("Testing with numbers 123")]
    [TestCase("Special characters !@#$%")]
    public void ContainsBannedWords_WhenMessageContainsNoBannedWords_ShouldReturnFalse(string message)
    {
        var result = _wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.False);
    }

    [TestCase("This message contains spam")]
    [TestCase("spam is everywhere")]
    [TestCase("I hate spam")]
    [TestCase("spamspamspam")]
    public void ContainsBannedWords_WhenMessageContainsSpam_ShouldReturnTrue(string message)
    {
        var result = _wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.True);
    }

    [TestCase("This is a badword example")]
    [TestCase("badword should be filtered")]
    [TestCase("No badword allowed")]
    public void ContainsBannedWords_WhenMessageContainsBadword_ShouldReturnTrue(string message)
    {
        var wordFilter = new WordFilter(["badword"]);
        var result = wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.True);
    }

    [TestCase("This content is inappropriate")]
    [TestCase("inappropriate behavior detected")]
    [TestCase("Mark as inappropriate")]
    public void ContainsBannedWords_WhenMessageContainsInappropriate_ShouldReturnTrue(string message)
    {
        var wordFilter = new WordFilter(["inappropriate"]);
        var result = wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.True);
    }

    [TestCase("SPAM", "spam")]
    [TestCase("Spam", "spam")]
    [TestCase("SpAm", "spam")]
    [TestCase("BADWORD", "badword")]
    [TestCase("BadWord", "badword")]
    [TestCase("INAPPROPRIATE", "inappropriate")]
    [TestCase("Inappropriate", "inappropriate")]
    [TestCase("InApPrOpRiAtE", "inappropriate")]
    public void ContainsBannedWords_WhenBannedWordInDifferentCase_ShouldReturnTrue(string messageWord, string bannedWord)
    {
        var wordFilter = new WordFilter([bannedWord]);
        var result = wordFilter.ContainsBannedWords($"This message contains {messageWord}");

        Assert.That(result, Is.True);
    }

    [TestCase("This spam message is inappropriate and contains badword")]
    [TestCase("spam badword inappropriate")]
    [TestCase("Multiple: spam, badword, inappropriate")]
    public void ContainsBannedWords_WhenMessageContainsMultipleBannedWords_ShouldReturnTrue(string message)
    {
        // Use a filter with all three banned words to ensure we're testing multiple matches
        var wordFilter = new WordFilter(["spam", "badword", "inappropriate"]);
        var result = wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.True);
    }

    [TestCase("spammer", "spam")]
    [TestCase("badwords", "badword")]
    [TestCase("inappropriately", "inappropriate")]
    public void ContainsBannedWords_WhenMessageContainsSubstring_ShouldReturnTrue(string messageWord, string bannedWord)
    {
        // This test verifies that substrings are detected (e.g., "spam" is found in "spammer")
        var wordFilter = new WordFilter([bannedWord]);
        var message = $"This message contains {messageWord}";
        var result = wordFilter.ContainsBannedWords(message);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsBannedWords_WhenNoBannedWords_ShouldReturnFalse()
    {
        var wordFilter = new WordFilter([]);
        var result = wordFilter.ContainsBannedWords("This message contains spam badword inappropriate");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenBannedWordsArrayContainsEmptyStrings_ShouldHandleGracefully()
    {
        var wordFilter = new WordFilter([string.Empty, "spam", string.Empty]);
        var result = wordFilter.ContainsBannedWords("This message contains spam");

        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsBannedWords_WhenBannedWordsArrayContainsWhitespace_ShouldHandleGracefully()
    {
        var wordFilter = new WordFilter([" ", "spam", "\t"]);
        var result = wordFilter.ContainsBannedWords("This message contains spam");

        Assert.That(result, Is.True);
    }

    [TestCase("sp")]
    [TestCase("am")]
    [TestCase("bad")]
    [TestCase("word")]
    [TestCase("in")]
    [TestCase("appropriate")]
    public void ContainsBannedWords_WhenMessageContainsPartialMatch_ShouldReturnFalse(string partialWord)
    {
        var result = _wordFilter.ContainsBannedWords($"This message contains {partialWord}");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageContainsUnicodeCharacters_ShouldWorkCorrectly()
    {
        var wordFilter = new WordFilter(["тест", "спам"]);
        var result = wordFilter.ContainsBannedWords("This message contains спам content");

        Assert.That(result, Is.True);
    }

    [Test]
    public void ContainsBannedWords_WhenMessageIsVeryLong_ShouldWorkCorrectly()
    {
        var longMessage = new string('a', 10000) + "spam" + new string('b', 10000);
        var result = _wordFilter.ContainsBannedWords(longMessage);

        Assert.That(result, Is.True);
    }
}
