using Pacos.Constants;
using Pacos.Services.GenerativeAi;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Pacos.Services.ChatCommandHandlers;

public class ResetHandler
{
    private readonly ILogger<ResetHandler> _logger;
    private readonly ChatService _chatService;

    public ResetHandler(
        ILogger<ResetHandler> logger,
        ChatService chatService)
    {
        _logger = logger;
        _chatService = chatService;
    }

    public async Task HandleResetAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        string author,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing {Command} command from {Author} in chat {ChatId}", Const.ResetCommand, author, updateMessage.Chat.Id);
        await _chatService.ResetChatHistoryAsync(updateMessage.Chat.Id);
        await botClient.SendMessage(
            chatId: updateMessage.Chat.Id,
            text: "Chat history has been reset for this chat.",
            replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
            cancellationToken: cancellationToken);
        _logger.LogInformation("Sent chat history reset confirmation to {Author} in chat {ChatId}", author, updateMessage.Chat.Id);
    }
}
