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

    public async Task<string> GetResponseAsync(long chatId, string message)
    {
        await _semaphoreSlim.WaitAsync();
        try
        {
            var chatHistory = _chatHistories.GetOrAdd(chatId, _ => [new ChatMessage(ChatRole.System, Const.SystemPrompt)]);

            if (chatHistory.Sum(x => x.Text.Length) + message.Length is var numberOfCharacters and > Const.MaxAllowedContextLength)
            {
                _logger.LogWarning("Chat history is too long ({NumberOfCharacters} characters), clearing history", numberOfCharacters);
                chatHistory.Clear();
                chatHistory.Add(new ChatMessage(ChatRole.System, Const.SystemPrompt));
            }

            chatHistory.Add(new ChatMessage(ChatRole.User, message));

            var responseObject = await _chatClient.GetResponseAsync(chatHistory);
            var responseText = responseObject.Text;

            chatHistory.Add(new ChatMessage(ChatRole.Assistant, responseText));

            return responseText;
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
                _logger.LogInformation("Chat history for chat ID {ChatId} has been reset.", chatId);
                // Optionally, re-initialize with system prompt if needed immediately after reset
                // _chatHistories.TryAdd(chatId, [new ChatMessage(ChatRole.System, Const.SystemPrompt)]);
            }
            else
            {
                _logger.LogInformation("No chat history found for chat ID {ChatId} to reset.", chatId);
            }
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
