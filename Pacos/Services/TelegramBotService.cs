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
    private readonly GenerativeModelService _generativeModelService;

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
        ChatService chatService,
        GenerativeModelService generativeModelService)
    {
        _logger = logger;
        _options = options;
        _telegramBotClient = telegramBotClient;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _taskQueue = taskQueue;
        _wordFilter = wordFilter;
        _chatService = chatService;
        _generativeModelService = generativeModelService;
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
            if (update is { Type: UpdateType.Message, Message: { ForwardFrom: null, ForwardFromChat: null, ForwardSignature: null, From: not null } }
                && update.Message.IsAutomaticForward != true
                && _options.Value.AllowedChatIds.Any(chatId => chatId == update.Message.Chat.Id))
            {
                var author = update.Message.From.Username ?? string.Join(' ', update.Message.From.FirstName, update.Message.From.LastName).Trim();
                var message = (update.Message.Text ?? update.Message.Caption ?? string.Empty).Trim();

                // Handle draw command
                if (message.StartsWith(Const.DrawCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    var prompt = message.Substring(Const.DrawCommand.Length).Trim();
                    _logger.LogInformation("Processing {Command} command from {Author} with prompt: {Prompt}", Const.DrawCommand, author, prompt);

                    if (update.Message.Photo is { Length: > 0 })
                    {
                        // Image-to-Image
                        var photoSize = update.Message.Photo.Last();
                        var fileInfo = await botClient.GetFile(photoSize.FileId, cancellationToken);

                        await using var memoryStream = new MemoryStream();
                        await botClient.DownloadFile(fileInfo.FilePath ?? string.Empty, memoryStream, cancellationToken);
                        var imageBytes = memoryStream.ToArray();
                        var mimeType = fileInfo.FilePath?.Split('.').LastOrDefault() switch
                        {
                            "png" => "image/png",
                            "webp" => "image/webp",
                            _ => "image/jpeg",
                        };

                        var (generatedImageData, error) = await _generativeModelService.GenerateImageToImageAsync(prompt, imageBytes, mimeType);
                        if (generatedImageData != null)
                        {
                            await botClient.SendPhoto(
                                chatId: update.Message.Chat.Id,
                                photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                                caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogInformation("Sent image-to-image result to {Author}", author);
                        }
                        else
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Sorry, couldn't generate image from image: {error}",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogWarning("Failed image-to-image for {Author}: {Error}", author, error);
                        }
                    }
                    else
                    {
                        // Text-to-Image
                        if (string.IsNullOrWhiteSpace(prompt))
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Please provide a prompt for {Const.DrawCommand}. Example: {Const.DrawCommand} a cat wearing a hat",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            return;
                        }

                        var (generatedImageData, error) = await _generativeModelService.GenerateTextToImageAsync(prompt);
                        if (generatedImageData != null)
                        {
                            await botClient.SendPhoto(
                                chatId: update.Message.Chat.Id,
                                photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                                caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogInformation("Sent text-to-image result to {Author}", author);
                        }
                        else
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Sorry, couldn't generate image from text: {error}",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogWarning("Failed text-to-image for {Author}: {Error}", author, error);
                        }
                    }
                }
                // Existing mention-based logic
                else if (Const.Mentions.Any(mention => message.StartsWith(mention, StringComparison.InvariantCultureIgnoreCase)))
                {
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
                        parseMode: ParseMode.None,
                        replyParameters: new ReplyParameters
                        {
                            MessageId = update.Message.MessageId,
                        },
                        cancellationToken: cancellationToken);
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
