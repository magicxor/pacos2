using Microsoft.Extensions.Options;
using NTextCat;
using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models.Options;
using Pacos.Services.BackgroundTasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

public class TelegramBotService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IOptions<PacosOptions> _options;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly RankedLanguageIdentifier _rankedLanguageIdentifier;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly WordFilter _wordFilter;
    private readonly ChatService _chatService;

    private static readonly ReceiverOptions ReceiverOptions = new()
    {
        // receive all update types
        AllowedUpdates = [],
    };

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        IOptions<PacosOptions> options,
        ITelegramBotClient telegramBotClient,
        RankedLanguageIdentifier rankedLanguageIdentifier,
        IBackgroundTaskQueue taskQueue,
        WordFilter wordFilter,
        ChatService chatService)
    {
        _logger = logger;
        _options = options;
        _telegramBotClient = telegramBotClient;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _taskQueue = taskQueue;
        _wordFilter = wordFilter;
        _chatService = chatService;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received update with type={Update}", update.Type.ToString());

        await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            await HandleUpdateFunctionAsync(botClient, update, token));
    }

    private async Task HandleUpdateFunctionAsync(ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            if (update is { Type: UpdateType.Message, Message: { Text: { Length: > 0 } updateMessageText, ForwardFrom: null, ForwardFromChat: null, ForwardSignature: null, From: not null } }
                && update.Message.IsAutomaticForward != true
                && _options.Value.AllowedChatIds.Any(chatId => chatId == update.Message.Chat.Id)
                && Const.Mentions.Any(mention => updateMessageText.StartsWith(mention, StringComparison.InvariantCultureIgnoreCase)))
            {
                var author = update.Message.From.Username ?? string.Join(' ', update.Message.From.FirstName, update.Message.From.LastName).Trim();
                var message = updateMessageText.Trim();

                var language = _rankedLanguageIdentifier.Identify(message).FirstOrDefault();
                var languageCode = language?.Item1?.Iso639_3 ?? "eng";

                _logger.LogInformation("Processing the prompt from {Author} (lang={LanguageCode}): {UpdateMessageTextTrimmed}",
                    author, languageCode, message);

                var replyText = string.Empty;

                try
                {
                    replyText = message switch
                    {
                        _ when languageCode is not "rus" and not "eng" => "хуйню спизданул",
                        _ when _wordFilter.ContainsBannedWords(message) => "ты пидор, кстати",
                        _ => await _chatService.GetResponseAsync(update.Message.Chat.Id, $"{author}: {message}"),
                    };
                    replyText = replyText.Cut(Const.MaxTelegramMessageLength);
                }
                catch (Exception e)
                {
                    replyText = e.ToString();
                }

                _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

                await botClient.SendMessage(new ChatId(update.Message.Chat.Id),
                    replyText,
                    ParseMode.None,
                    new ReplyParameters
                    {
                        MessageId = update.Message.MessageId,
                    },
                    cancellationToken: cancellationToken);
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
                @"Telegram API Error. ErrorCode={ErrorCode}, RetryAfter={RetryAfter}, MigrateToChatId={MigrateToChatId}",
                apiRequestException.ErrorCode,
                apiRequestException.Parameters?.RetryAfter,
                apiRequestException.Parameters?.MigrateToChatId);
        }
        else
        {
            _logger.LogError(exception, @"Telegram API Error");
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
