namespace Pacos.Services;

public class WordFilter
{
    private readonly string[] _bannedWords;

    public WordFilter(string[] bannedWords)
    {
        _bannedWords = bannedWords;
    }

    public bool ContainsBannedWords(string message)
    {
        return !string.IsNullOrWhiteSpace(message)
               && _bannedWords.Any(bannedWord => message.Contains(bannedWord, StringComparison.OrdinalIgnoreCase));
    }
}
