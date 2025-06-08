using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Pacos.Constants;

namespace Pacos.Services;

public class ChatService
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

    public async Task<(string Text, IReadOnlyCollection<DataContent> DataContents)> GetResponseAsync(
        long chatId,
        long messageId,
        string authorName,
        string messageText,
        byte[]? inputImageBytes = null,
        string? inputImageMimeType = null)
    {
        await _semaphoreSlim.WaitAsync();
        try
        {
            var chatHistory = _chatHistories.GetOrAdd(chatId, _ => [new ChatMessage(ChatRole.System, Const.SystemPrompt)]);

            if (chatHistory.Sum(x => x.Text.Length) + messageText.Length is var numberOfCharacters and > Const.MaxAllowedContextLength)
            {
                _logger.LogWarning("Chat history is too long ({NumberOfCharacters} characters), clearing history", numberOfCharacters);
                chatHistory.Clear();
                chatHistory.Add(new ChatMessage(ChatRole.System, Const.SystemPrompt));
            }

            var inputContents = new List<AIContent> { new TextContent(messageText) };
            if (inputImageBytes is not null && inputImageMimeType is not null)
            {
                inputContents.Add(new DataContent(inputImageBytes, inputImageMimeType));
            }

            var userMessage = new ChatMessage
            {
                MessageId  = messageId.ToString(),
                AuthorName = authorName,
                Contents = inputContents,
                Role = ChatRole.User,
            };

            chatHistory.Add(userMessage);

            var responseObject = await _chatClient.GetResponseAsync(chatHistory);
            var responseText = responseObject.Text;
            var dataContents = responseObject.Messages
                .SelectMany(x => x.Contents
                    .OfType<DataContent>())
                .ToList()
                .AsReadOnly();

            chatHistory.AddRange(responseObject.Messages);

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
}
