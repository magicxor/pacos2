using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.AI;
using Pacos.Constants;
using Pacos.Models;

namespace Pacos.Services.GenerativeAi;

public sealed class ChatService : IDisposable
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatClient _chatClient;
    private readonly IChatClient? _fallbackChatClient;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<long, List<ChatMessage>> _chatHistories = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatSemaphores = new();

    public ChatService(
        ILogger<ChatService> logger,
        IChatClient chatClient,
        IChatClient? fallbackChatClient,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _chatClient = chatClient;
        _fallbackChatClient = fallbackChatClient;
        _timeProvider = timeProvider;
    }

    public bool HasFallback => _fallbackChatClient is not null;

    private ChatMessage GetSystemPrompt(bool isGroupChat, string? previousChatSummary = null)
    {
        var systemPrompt = Const.SystemPrompt
                           + (isGroupChat
                               ? Environment.NewLine + Environment.NewLine + Const.GroupChatRuleSystemPrompt
                               : Environment.NewLine + Environment.NewLine + Const.PersonalChatRuleSystemPrompt)
                           + Environment.NewLine
                           + Environment.NewLine
                           + $"Дата начала текущей сессии: {_timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd hh:mm:ss", CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(previousChatSummary))
        {
            systemPrompt += Environment.NewLine
                            + Environment.NewLine
                            + "Краткое резюме предыдущей истории чата: " + previousChatSummary;
        }

        return new ChatMessage(ChatRole.System, systemPrompt);
    }

    private static ChatMessage GetSumUserPrompt()
    {
        return new ChatMessage(ChatRole.User, Const.SummarizationPrompt);
    }

    private SemaphoreSlim GetOrCreateChatSemaphore(long chatId)
    {
        return _chatSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
    }

    public async Task<ChatResponseInfo> GetResponseAsync(
        long chatId,
        bool isGroupChat,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null,
        bool useFallback = false)
    {
        var chatSemaphore = GetOrCreateChatSemaphore(chatId);
        await chatSemaphore.WaitAsync();

        try
        {
            var chatHistory = _chatHistories.GetOrAdd(chatId, _ => [GetSystemPrompt(isGroupChat)]);
            var wasHistorySummarized = false;
            var wasSummarizationFailed = false;

            if (chatHistory.Sum(x => x.Text.Length) + messageText.Length is var numberOfCharacters and > Const.MaxAllowedContextLength)
            {
                _logger.LogInformation("Chat history is too long ({NumberOfCharacters} characters), clearing history", numberOfCharacters);

                try
                {
                    // summarize chat history instead of clearing it
                    var summarizedResponse = await _chatClient.GetResponseAsync(
                        [..chatHistory, GetSumUserPrompt()]);

                    _logger.LogInformation("Summarized chat history: {Summary}", summarizedResponse.Text);

                    chatHistory.Clear();
                    chatHistory.Add(GetSystemPrompt(isGroupChat, summarizedResponse.Text));

                    wasHistorySummarized = true;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to summarize chat history for chat ID {ChatId}", chatId);
                    wasSummarizationFailed = true;

                    chatHistory.Clear();
                    chatHistory.Add(GetSystemPrompt(isGroupChat));
                }
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

            var client = GetChatClient(useFallback);

            var responseObject = await client.GetResponseAsync(chatHistory.Concat([userMessage]));

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
                responseText = $"{(functionCallCount > 1 ? functionCallCount : string.Empty)}🔧 " + responseText;

                var functionCallsSerialized = functionCalls.Select(x => $"{x.Name} ({string.Join(", ", x.Arguments?.Select(a => $"{a.Key}: {a.Value}") ?? [])})");
                var functionCallsString = string.Join(", ", $"[{functionCallsSerialized}]");
                _logger.LogInformation("Function calls: {FunctionCalls}", functionCallsString);
            }

            if (wasHistorySummarized)
            {
                responseText = "🗜️ " + responseText;
            }

            if (wasSummarizationFailed)
            {
                responseText = "📝❌ " + responseText;
            }

            if (useFallback)
            {
                responseText = "💥♿ " + responseText;
            }

            return new ChatResponseInfo(responseText, dataContents);
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

    private IChatClient GetChatClient(bool useFallback)
    {
        if (useFallback && _fallbackChatClient is not null)
        {
            return _fallbackChatClient;
        }

        return _chatClient;
    }

    public void Dispose()
    {
        _chatClient.Dispose();
        _fallbackChatClient?.Dispose();

        // Dispose all chat semaphores
        foreach (var semaphore in _chatSemaphores.Values)
        {
            semaphore.Dispose();
        }
        _chatSemaphores.Clear();
    }
}
