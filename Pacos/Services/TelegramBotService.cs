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

    private bool Assign(string source, ref string destination)
    {
        destination = source;
        return true;
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
                var currentMention = Const.Mentions.FirstOrDefault(mention => message.StartsWith(mention, StringComparison.InvariantCultureIgnoreCase));

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
                // Handle reset command
                else if (message.Equals(Const.ResetCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogInformation("Processing {Command} command from {Author} in chat {ChatId}", Const.ResetCommand, author, update.Message.Chat.Id);
                    await _chatService.ResetChatHistoryAsync(update.Message.Chat.Id);
                    await botClient.SendMessage(
                        chatId: update.Message.Chat.Id,
                        text: "Chat history has been reset for this chat.",
                        replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                        cancellationToken: cancellationToken);
                    _logger.LogInformation("Sent chat history reset confirmation to {Author} in chat {ChatId}", author, update.Message.Chat.Id);
                }
                // Handle command for video generation
                else if (message.StartsWith(Const.VideoCommand, StringComparison.InvariantCultureIgnoreCase))
                {
                    var prompt = message.Substring(Const.VideoCommand.Length).Trim();
                    _logger.LogInformation("Processing {Command} command from {Author} with prompt: {Prompt}", Const.VideoCommand, author, prompt);

                    if (update.Message.Photo != null && update.Message.Photo.Length > 0)
                    {
                        // Image-to-Video
                        var photoSize = update.Message.Photo.Last();
                        var fileInfo = await botClient.GetFile(photoSize.FileId, cancellationToken);

                        await using var memoryStream = new MemoryStream();
                        await botClient.DownloadFile(fileInfo.FilePath ?? string.Empty, memoryStream, cancellationToken);
                        var imageBytes = memoryStream.ToArray();
                        var mimeType = fileInfo.FilePath?.Split('.').LastOrDefault() switch
                        {
                            "png" => "image/png",
                            "webp" => "image/webp",
                            _ => "image/jpeg"
                        };

                        var (generatedVideoData, error) = await _generativeModelService.GenerateImageToVideoAsync(prompt, imageBytes, mimeType);
                        if (generatedVideoData != null)
                        {
                            await botClient.SendVideo(
                                chatId: update.Message.Chat.Id,
                                video: new InputFileStream(new MemoryStream(generatedVideoData), "generated_video.mp4"),
                                caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogInformation("Sent image-to-video result to {Author}", author);
                        }
                        else
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Sorry, couldn't generate video from image: {error}\n(Note: Video model support is experimental and may not be fully configured.)",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogWarning("Failed image-to-video for {Author}: {Error}", author, error);
                        }
                    }
                    else
                    {
                        // Text-to-Video
                        if (string.IsNullOrWhiteSpace(prompt))
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Please provide a prompt for {Const.VideoCommand}. Example: {Const.VideoCommand} a running dog",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            return;
                        }

                        var (generatedVideoData, error) = await _generativeModelService.GenerateTextToVideoAsync(prompt);
                        if (generatedVideoData != null)
                        {
                            await botClient.SendVideo(
                                chatId: update.Message.Chat.Id,
                                video: new InputFileStream(new MemoryStream(generatedVideoData), "generated_video.mp4"),
                                caption: prompt.Cut(Const.MaxTelegramCaptionLength),
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogInformation("Sent text-to-video result to {Author}", author);
                        }
                        else
                        {
                            await botClient.SendMessage(
                                chatId: update.Message.Chat.Id,
                                text: $"Sorry, couldn't generate video from text: {error}\n(Note: Video model support is experimental and may not be fully configured.)",
                                replyParameters: new ReplyParameters { MessageId = update.Message.MessageId },
                                cancellationToken: cancellationToken);
                            _logger.LogWarning("Failed text-to-video for {Author}: {Error}", author, error);
                        }
                    }
                }
                // Existing mention-based logic
                else if (!string.IsNullOrEmpty(currentMention))
                {
                    // Remove the mention from the message
                    message = message.Substring(currentMention.Length).TrimStart(',', ' ', '.', '!', '?', ':', ';').Trim();
                    if (string.IsNullOrEmpty(message))
                    {
                        return;
                    }

                    var language = _rankedLanguageIdentifier.Identify(message).FirstOrDefault();
                    var languageCode = language?.Item1?.Iso639_3 ?? "eng";

                    // Determine the full message to send to the LLM, including replied-to message if present
                    string fullMessageToLLM;
                    string originalMessageLogInfo = string.Empty;

                    if (update.Message.ReplyToMessage != null)
                    {
                        var repliedToMessageText = (update.Message.ReplyToMessage.Text ?? update.Message.ReplyToMessage.Caption ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(repliedToMessageText))
                        {
                            var repliedToAuthor = update.Message.ReplyToMessage.From?.Username ??
                                                  string.Join(' ', update.Message.ReplyToMessage.From?.FirstName, update.Message.ReplyToMessage.From?.LastName).Trim();
                            if (string.IsNullOrWhiteSpace(repliedToAuthor))
                            {
                                repliedToAuthor = "Original Poster"; // Fallback if author is not available
                            }

                            fullMessageToLLM = $"{author} (replying to {repliedToAuthor}): {message}\n\n--- Original Message by {repliedToAuthor}: ---\n{repliedToMessageText}";
                            originalMessageLogInfo = $" | Original by {repliedToAuthor}: \"{repliedToMessageText.Cut(50)}\""; // Cut for brevity in logs
                        }
                        else
                        {
                            fullMessageToLLM = $"{author}: {message}"; // ReplyToMessage exists but has no text/caption
                        }
                    }
                    else
                    {
                        fullMessageToLLM = $"{author}: {message}"; // Not a reply
                    }

                    _logger.LogInformation("Processing prompt from {Author} (lang={LanguageCode}): \"{UserMessage}\"{OriginalMessageLog}",
                        author, languageCode, message, originalMessageLogInfo); // Log user's message and info about replied message

                    var replyText = string.Empty;

                    try
                    {
                        // The switch for language/banned words still operates on the user's current message (`message`)
                        replyText = message switch
                        {
                            _ when languageCode is not "rus" and not "eng" => "хуйню спизданул",
                            _ when _wordFilter.ContainsBannedWords(message) => "ты пидор, кстати",
                            // Pass the potentially combined message to the chat service
                            _ => await _chatService.GetResponseAsync(update.Message.Chat.Id, fullMessageToLLM),
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
