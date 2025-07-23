using Pacos.Extensions;

namespace Pacos.Tests.Unit;

[TestFixture]
[Parallelizable(scope: ParallelScope.All)]
internal sealed class StringExtensionsTests
{
    [Test]
    public void TakeLeft_WhenNull_ShouldReturnNull()
    {
        const string? source = null;
        var result = source.TakeLeft(1);
        Assert.That(result, Is.Null);
    }

    [TestCase("", 0, "")]
    [TestCase("", 1, "")]
    [TestCase("a", 0, "")]
    [TestCase("a", 1, "a")]
    [TestCase("a", 2, "a")]
    [TestCase("a", 3, "a")]
    [TestCase("abc", 0, "")]
    [TestCase("abc", 1, "a")]
    [TestCase("abc", 2, "ab")]
    [TestCase("abc", 3, "abc")]
    [TestCase("abc", 4, "abc")]
    [TestCase("abc", 999, "abc")]
    public void TakeLeft_WhenArgumentsValid_ShouldReturnExpectedResult(string source, int maxLength, string expectedResult)
    {
        var actualResult = source.TakeLeft(maxLength);
        Assert.That(actualResult, Is.EqualTo(expectedResult));
    }

    [TestCase("", -1)]
    [TestCase("a", -2)]
    public void TakeLeft_WhenMaxLengthIsLessThanZero_ShouldThrowArgumentOutOfRangeException(string source, int maxLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = source.TakeLeft(maxLength));
    }

    [Test]
    public void TakeRight_WhenNull_ShouldReturnNull()
    {
        const string? source = null;
        var result = source.TakeRight(1);
        Assert.That(result, Is.Null);
    }

    [TestCase("", 0, "")]
    [TestCase("", 1, "")]
    [TestCase("a", 0, "")]
    [TestCase("a", 1, "a")]
    [TestCase("a", 2, "a")]
    [TestCase("a", 3, "a")]
    [TestCase("abc", 0, "")]
    [TestCase("abc", 1, "c")]
    [TestCase("abc", 2, "bc")]
    [TestCase("abc", 3, "abc")]
    [TestCase("abc", 4, "abc")]
    [TestCase("abc", 999, "abc")]
    public void TakeRight_WhenArgumentsValid_ShouldReturnExpectedResult(string source, int maxLength, string expectedResult)
    {
        var actualResult = source.TakeRight(maxLength);
        Assert.That(actualResult, Is.EqualTo(expectedResult));
    }

    [TestCase("", -1)]
    [TestCase("a", -2)]
    public void TakeRight_WhenMaxLengthIsLessThanZero_ShouldThrowArgumentOutOfRangeException(string source, int maxLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = source.TakeRight(maxLength));
    }

    [TestCase(null, null, true)]
    [TestCase(null, "", false)]
    [TestCase("", null, false)]
    [TestCase("", "", true)]
    [TestCase("test", "TEST", true)]
    [TestCase("test", "test", true)]
    [TestCase("test", "other", false)]
    public void EqualsIgnoreCase_ShouldReturnExpectedResult(string? source, string? target, bool expectedResult)
    {
        var actualResult = source.EqualsIgnoreCase(target);
        Assert.That(actualResult, Is.EqualTo(expectedResult));
    }

    [Test]
    public void IsNotNullOrEmpty_WhenNull_ShouldReturnFalse()
    {
        const string? value = null;
        Assert.That(value.IsNotNullOrEmpty(), Is.False);
    }

    [Test]
    public void IsNotNullOrEmpty_WhenEmpty_ShouldReturnFalse()
    {
        Assert.That(string.Empty.IsNotNullOrEmpty(), Is.False);
    }

    [Test]
    public void IsNotNullOrEmpty_WhenNonEmpty_ShouldReturnTrue()
    {
        Assert.That("test".IsNotNullOrEmpty(), Is.True);
    }

    [Test]
    public void IsNullOrEmpty_WhenNull_ShouldReturnTrue()
    {
        const string? value = null;
        Assert.That(value.IsNullOrEmpty(), Is.True);
    }

    [Test]
    public void IsNullOrEmpty_WhenEmpty_ShouldReturnTrue()
    {
        Assert.That(string.Empty.IsNullOrEmpty(), Is.True);
    }

    [Test]
    public void IsNullOrEmpty_WhenNonEmpty_ShouldReturnFalse()
    {
        Assert.That("test".IsNullOrEmpty(), Is.False);
    }

    [Test]
    public void Cut_WhenNull_ShouldReturnNull()
    {
        const string? source = null;
        var result = source.Cut(10);
        Assert.That(result, Is.Null);
    }

    [TestCase("", 0, "")]
    [TestCase("", 10, "")]
    [TestCase("abc", 10, "abc")] // Length > text length
    [TestCase("abcdef", 6, "abcdef")] // Length == text length
    [TestCase("abcdefghij", 10, "abcdefghij")] // Length == text length
    [TestCase("abcdefghijk", 10, "abcdefg...")] // Length < text length
    [TestCase("1234", 4, "1234")] // Length == text length
    [TestCase("12345", 4, "1...")] // Length < text length, length = 4
    [TestCase("1234", 3, "...")] // Length < text length, length = 3
    public void Cut_WhenTextLengthNotExceedsLengthOrLengthIs3_ShouldReturnExpected(string source, int maxLength, string expected)
    {
        var result = source.Cut(maxLength);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("abc", 0)]
    [TestCase("abc", 1)]
    [TestCase("abc", 2)]
    public void Cut_WhenLengthIsLessThan3_ShouldThrowArgumentOutOfRangeException(string source, int maxLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = source.Cut(maxLength));
    }
}
