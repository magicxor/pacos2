using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.AI;
using Pacos.Constants;

namespace Pacos.Services.GenerativeAi;

public sealed class ChatService : IDisposable
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatClient _chatClient;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<long, List<ChatMessage>> _chatHistories = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatSemaphores = new();

    public ChatService(
        ILogger<ChatService> logger,
        IChatClient chatClient,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _chatClient = chatClient;
        _timeProvider = timeProvider;
    }

    private ChatMessage GetSystemPrompt()
    {
        return new ChatMessage(
            ChatRole.System,
            Const.SystemPrompt + Environment.NewLine + Environment.NewLine + $"Дата начала текущей сессии: {_timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd hh:mm:ss", CultureInfo.InvariantCulture)}");
    }

    private SemaphoreSlim GetOrCreateChatSemaphore(long chatId)
    {
        return _chatSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
    }

    public async Task<(string Text, IReadOnlyCollection<DataContent> DataContents)> GetResponseAsync(
        long chatId,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null)
    {
        var chatSemaphore = GetOrCreateChatSemaphore(chatId);
        await chatSemaphore.WaitAsync();

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
                MessageId = messageId.ToString(CultureInfo.InvariantCulture),
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

            var functionCalls = responseObject.Messages
                .SelectMany(x => x.Contents
                    .OfType<FunctionCallContent>())
                .ToList()
                .AsReadOnly();

            var functionCallCount = functionCalls.Count;
            if (functionCallCount > 0)
            {
                responseText = $"[{functionCallCount}🔧] " + responseText;

                var functionCallsSerialized = functionCalls.Select(x => $"{x.Name} ({string.Join(", ", x.Arguments?.Select(a => $"{a.Key}: {a.Value}") ?? [])})");
                var functionCallsString = string.Join(", ", $"[{functionCallsSerialized}]");
                _logger.LogInformation("Function calls: {FunctionCalls}", functionCallsString);
            }

            return (responseText, dataContents);
        }
        finally
        {
            chatSemaphore.Release();
        }
    }

    public async Task ResetChatHistoryAsync(long chatId)
    {
        var chatSemaphore = GetOrCreateChatSemaphore(chatId);
        await chatSemaphore.WaitAsync();

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
            chatSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        
        // Dispose all chat semaphores
        foreach (var semaphore in _chatSemaphores.Values)
        {
            semaphore.Dispose();
        }
        _chatSemaphores.Clear();
    }
}
