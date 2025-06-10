using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.AI;
using Pacos.Constants;

namespace Pacos.Services;

public sealed class ChatService : IDisposable
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatClient _chatClient;
    private readonly ConcurrentDictionary<long, List<ChatMessage>> _chatHistories = new();
    private readonly SemaphoreSlim _semaphoreSlim = new(initialCount: 1, maxCount: 1);

    public ChatService(
        ILogger<ChatService> logger,
        IChatClient chatClient)
    {
        _logger = logger;
        _chatClient = chatClient;
    }

    private ChatMessage GetSystemPrompt()
    {
        return new ChatMessage(ChatRole.System, Const.SystemPrompt);
    }

    public async Task<(string Text, IReadOnlyCollection<DataContent> DataContents)> GetResponseAsync(
        long chatId,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null)
    {
        await _semaphoreSlim.WaitAsync();

        try
        {
            var chatHistory = _chatHistories.GetOrAdd(chatId, _ => [GetSystemPrompt()]);

            if (chatHistory.Sum(x => x.Text.Length) + messageText.Length is var numberOfCharacters and > Const.MaxAllowedContextLength)
            {
                _logger.LogWarning("Chat history is too long ({NumberOfCharacters} characters), clearing history", numberOfCharacters);
                chatHistory.Clear();
                chatHistory.Add(GetSystemPrompt());
            }

            var inputContents = new List<AIContent> { new TextContent(messageText) };
            if (fileBytes is not null && fileMimeType is not null)
            {
                inputContents.Add(new DataContent(fileBytes, fileMimeType));
            }

            var userMessage = new ChatMessage
            {
                MessageId  = messageId.ToString(CultureInfo.InvariantCulture),
                AuthorName = authorName,
                Contents = inputContents,
                Role = ChatRole.User,
            };

            var responseObject = await _chatClient.GetResponseAsync(chatHistory.Concat([userMessage]));

            chatHistory.Add(new ChatMessage(ChatRole.User, messageText));

            var responseText = responseObject.Text;
            var dataContents = responseObject.Messages
                .SelectMany(x => x.Contents
                    .OfType<DataContent>())
                .ToList()
                .AsReadOnly();

            chatHistory.Add(new ChatMessage(ChatRole.Assistant, responseText));

            return (responseText, dataContents);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public async Task ResetChatHistoryAsync(long chatId)
    {
        await _semaphoreSlim.WaitAsync();

        try
        {
            if (_chatHistories.TryRemove(chatId, out _))
            {
                _logger.LogInformation("Chat history for chat ID {ChatId} has been reset", chatId);
            }
            else
            {
                _logger.LogInformation("No chat history found for chat ID {ChatId} to reset", chatId);
            }
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        _semaphoreSlim.Dispose();
    }
}
