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
        AllowedUpdates = [UpdateType.Message],
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

    private async Task HandleDrawAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        string author,
        CancellationToken cancellationToken)
    {
        var prompt = messageText.Substring(Const.DrawCommand.Length).Trim();
        _logger.LogInformation("Processing {Command} command from {Author} with prompt: {Prompt}", Const.DrawCommand, author, prompt);

        PhotoSize[]? sourcePhotoSizes = null;
        string photoSourceMessageContext = "current command message"; // For logging

        // 1. Check current message for photo
        if (updateMessage.Photo is { Length: > 0 })
        {
            sourcePhotoSizes = updateMessage.Photo;
        }
        // 2. If no photo in current message, AND it's a reply, AND replied-to message has photo
        else if (updateMessage.ReplyToMessage?.Photo is { Length: > 0 })
        {
            sourcePhotoSizes = updateMessage.ReplyToMessage.Photo;
            photoSourceMessageContext = "replied-to message";
            _logger.LogInformation("No photo in !draw command by {Author}, attempting to use photo from replied-to message", author);
        }

        if (sourcePhotoSizes != null) // Indicates image-to-image is possible
        {
            // Image-to-Image
            _logger.LogInformation("Performing image-to-image for {Author} using photo from {PhotoSourceContext}", author, photoSourceMessageContext);
            var photoSize = sourcePhotoSizes.Last();
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

            var (generatedImageData, generatedImageMime, error) = await _generativeModelService.GenerateImageToImageAsync(prompt, imageBytes, mimeType);
            if (generatedImageData != null)
            {
                await botClient.SendPhoto(
                    chatId: updateMessage.Chat.Id,
                    photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                    caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId }, // Always reply to the command message ID
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Sent image-to-image result (photo from {PhotoSourceContext}) to {Author}", photoSourceMessageContext, author);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: $"Sorry, couldn't generate image from image (photo from {photoSourceMessageContext}): {error}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogWarning("Failed image-to-image for {Author} (photo from {PhotoSourceContext}): {Error}", author, photoSourceMessageContext, error);
            }
        }
        else // Fallback to Text-to-Image if no suitable photo found
        {
            // Text-to-Image
            if (string.IsNullOrWhiteSpace(prompt))
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: $"Please provide a prompt for {Const.DrawCommand}. Example: {Const.DrawCommand} a cat wearing a hat",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                return;
            }

            var (generatedImageData, generatedImageMime, error) = await _generativeModelService.GenerateTextToImageAsync(prompt);
            if (generatedImageData != null)
            {
                await botClient.SendPhoto(
                    chatId: updateMessage.Chat.Id,
                    photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                    caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Sent text-to-image result to {Author}", author);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: $"Sorry, couldn't generate image from text: {error}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogWarning("Failed text-to-image for {Author}: {Error}", author, error);
            }
        }
    }

    private async Task HandleResetAsync(
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

    private async Task HandleMentionAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        string author,
        string currentMention,
        CancellationToken cancellationToken)
    {
        // Remove the mention from the message
        messageText = messageText.Substring(currentMention.Length).TrimStart(',', ' ', '.', '!', '?', ':', ';').Trim();
        if (string.IsNullOrEmpty(messageText))
        {
            return;
        }

        var language = _rankedLanguageIdentifier.Identify(messageText).FirstOrDefault();
        var languageCode = language?.Item1?.Iso639_3 ?? "eng";

        // Determine the full message to send to the LLM, including replied-to message if present
        string fullMessageToLlm;
        string originalMessageLogInfo = string.Empty;

        if (updateMessage.ReplyToMessage != null)
        {
            var repliedToMessageText = (updateMessage.ReplyToMessage.Text ?? updateMessage.ReplyToMessage.Caption ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(repliedToMessageText))
            {
                var repliedToAuthor = updateMessage.ReplyToMessage.From?.Username ??
                                      string.Join(' ', updateMessage.ReplyToMessage.From?.FirstName, updateMessage.ReplyToMessage.From?.LastName).Trim();
                if (string.IsNullOrWhiteSpace(repliedToAuthor))
                {
                    repliedToAuthor = "Original Poster"; // Fallback if author is not available
                }

                fullMessageToLlm = $"{author} (replying to {repliedToAuthor}): {messageText}\n\n--- Original Message by {repliedToAuthor}: ---\n{repliedToMessageText}";
                originalMessageLogInfo = $" | Original by {repliedToAuthor}: \"{repliedToMessageText.Cut(50)}\""; // Cut for brevity in logs
            }
            else
            {
                fullMessageToLlm = $"{author}: {messageText}"; // ReplyToMessage exists but has no text/caption
            }
        }
        else
        {
            fullMessageToLlm = $"{author}: {messageText}"; // Not a reply
        }

        _logger.LogInformation("Processing prompt from {Author} (lang={LanguageCode}): \"{UserMessage}\"{OriginalMessageLog}",
            author, languageCode, messageText, originalMessageLogInfo); // Log user's message and info about replied message

        var mediaInfo = GetFileInfo(updateMessage) ?? GetFileInfo(updateMessage.ReplyToMessage);
        var mediaBytes = await DownloadMediaIfPresentAsync(mediaInfo?.FileId, botClient, cancellationToken);

        var replyText = string.Empty;

        try
        {
            // The switch for language/banned words still operates on the user's current message (`message`)
            replyText = messageText switch
            {
                _ when _options.Value.AllowedLanguageCodes.All(lc => lc != languageCode) => "хуйню спизданул",
                _ when _wordFilter.ContainsBannedWords(messageText) => "ты пидор, кстати",
                _ => (await _chatService.GetResponseAsync(
                        updateMessage.Chat.Id,
                        updateMessage.Id,
                        author,
                        fullMessageToLlm,
                        mediaBytes,
                        mediaInfo?.MimeType
                     )).Text,
            };
            replyText = replyText.Cut(Const.MaxTelegramMessageLength);
        }
        catch (Exception e)
        {
            replyText = e.ToString();
        }

        _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

        await botClient.SendMessage(new ChatId(updateMessage.Chat.Id),
            replyText,
            parseMode: ParseMode.None,
            replyParameters: new ReplyParameters
            {
                MessageId = updateMessage.MessageId,
            },
            cancellationToken: cancellationToken);
    }

    private async Task HandleUpdateFunctionAsync(
        ITelegramBotClient botClient,
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
                var currentMention = Const.Mentions.FirstOrDefault(mention => message.StartsWith(mention, StringComparison.OrdinalIgnoreCase));

                if (message.StartsWith(Const.DrawCommand, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleDrawAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (message.Equals(Const.ResetCommand, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleResetAsync(botClient, update.Message, message, author, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(currentMention))
                {
                    await HandleMentionAsync(botClient, update.Message, message, author, currentMention, cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while handling update");
        }
    }

    private static (string FileId, string MimeType)? GetFileInfo(Message? message)
    {
        return message switch
        {
            { Photo: [.., var lastPhoto] } => (lastPhoto.FileId, "image/jpeg"),
            { Video: { } video } => (video.FileId, video.MimeType ?? "video/mp4"),
            { VideoNote: { } videoNote } => (videoNote.FileId, "video/mp4"),
            { Audio: { } audio } => (audio.FileId, audio.MimeType ?? "audio/mpeg"),
            { Voice: { } voice } => (voice.FileId, voice.MimeType ?? "audio/ogg"),
            { Animation: { } animation } => (animation.FileId, animation.MimeType ?? "video/mp4"),
            { Sticker: { } sticker } => (sticker.FileId, sticker.Type == StickerType.Regular ? "image/webp" : "application/octet-stream"),
            _ => null,
        };
    }

    private async Task<byte[]?> DownloadMediaIfPresentAsync(
        string? fileId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fileId))
            return null;

        _logger.LogDebug("Downloading media with fileId={FileId}", fileId);
        var fileInfo = await botClient.GetFile(fileId, cancellationToken);

        if (string.IsNullOrEmpty(fileInfo.FilePath))
        {
            _logger.LogWarning("FilePath is null or empty for fileId={FileId}. Cannot download media", fileId);
            return null;
        }

        try
        {
            await using var memoryStream = new MemoryStream();
            await botClient.DownloadFile(fileInfo.FilePath, memoryStream, cancellationToken);
            var downloadedImageBytes = memoryStream.ToArray();
            _logger.LogInformation("Successfully downloaded media with fileId={FileId}, size={Size} bytes", fileId, downloadedImageBytes.Length);
            return downloadedImageBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media with fileId={FileId}", fileId);
            return null;
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
