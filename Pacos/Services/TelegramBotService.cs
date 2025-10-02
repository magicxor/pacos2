using Microsoft.Extensions.Options;
using Pacos.Constants;
using Pacos.Models.Options;
using Pacos.Services.BackgroundTasks;
using Pacos.Services.ChatCommandHandlers;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

public sealed class TelegramBotService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IOptions<PacosOptions> _options;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly DrawHandler _drawHandler;
    private readonly ResetHandler _resetHandler;
    private readonly MentionHandler _mentionHandler;

    private static readonly ReceiverOptions ReceiverOptions = new()
    {
        AllowedUpdates = [UpdateType.Message],
    };

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        IOptions<PacosOptions> options,
        ITelegramBotClient telegramBotClient,
        IBackgroundTaskQueue taskQueue,
        DrawHandler drawHandler,
        ResetHandler resetHandler,
        MentionHandler mentionHandler)
    {
        _logger = logger;
        _options = options;
        _telegramBotClient = telegramBotClient;
        _taskQueue = taskQueue;
        _drawHandler = drawHandler;
        _resetHandler = resetHandler;
        _mentionHandler = mentionHandler;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update with type={Update}", update.Type.ToString());

        await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            await HandleUpdateFunctionAsync(botClient, update, token));
    }

    private async Task HandleUpdateFunctionAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            if (update is { Type: UpdateType.Message, Message: { ForwardFrom: null, ForwardFromChat: null, ForwardSignature: null, From: not null, IsAutomaticForward: false } }
                && _options.Value.AllowedChatIds.Any(chatId => chatId == update.Message.Chat.Id))
            {
                var author = update.Message.From.Username ?? string.Join(' ', update.Message.From.FirstName, update.Message.From.LastName).Trim();
                var message = (update.Message.Text ?? update.Message.Caption ?? string.Empty).Trim();
                var currentMention = Const.Mentions.FirstOrDefault(mention => message.StartsWith(mention, StringComparison.OrdinalIgnoreCase));

                if (message.StartsWith(Const.DrawCommand, StringComparison.OrdinalIgnoreCase))
                {
                    await _drawHandler.HandleDrawAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (message.Equals(Const.ResetCommand, StringComparison.OrdinalIgnoreCase))
                {
                    await _resetHandler.HandleResetAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(currentMention))
                {
                    var isGroupChat = update.Message.Chat.Type is ChatType.Group or ChatType.Supergroup;
                    await _mentionHandler.HandleMentionAsync(botClient, update.Message, message, isGroupChat, author, currentMention, cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while handling update");
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ApiRequestException apiRequestException)
        {
            _logger.LogError(exception,
                "Telegram API Error. ErrorCode={ErrorCode}, RetryAfter={RetryAfter}, MigrateToChatId={MigrateToChatId}",
                apiRequestException.ErrorCode,
                apiRequestException.Parameters?.RetryAfter,
                apiRequestException.Parameters?.MigrateToChatId);
        }
        else
        {
            _logger.LogError(exception, "Telegram API Error");
        }

        return Task.CompletedTask;
    }

    public void Start(CancellationToken cancellationToken)
    {
        _telegramBotClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: ReceiverOptions,
            cancellationToken: cancellationToken
        );
    }
}
