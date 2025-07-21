namespace Pacos.Services;

public sealed class WordFilter
{
    private readonly string[] _bannedWords;

    public WordFilter(string[] bannedWords)
    {
        _bannedWords = bannedWords.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    }

    public bool ContainsBannedWords(string message)
    {
        return !string.IsNullOrWhiteSpace(message)
               && _bannedWords.Any(bannedWord => message.Contains(bannedWord, StringComparison.OrdinalIgnoreCase));
    }
}
