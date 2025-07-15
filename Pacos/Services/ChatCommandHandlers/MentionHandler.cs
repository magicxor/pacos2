using GenerativeAI.Exceptions;
using Microsoft.Extensions.AI;
using NTextCat;
using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models;
using Pacos.Services.GenerativeAi;
using Pacos.Services.Markdown;
using Pacos.Services.VideoConversion;
using Polly;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class MentionHandler
{
    private readonly ILogger<MentionHandler> _logger;
    private readonly RankedLanguageIdentifier _rankedLanguageIdentifier;
    private readonly WordFilter _wordFilter;
    private readonly ChatService _chatService;
    private readonly MarkdownConversionService _markdownConversionService;
    private readonly VideoConverter _videoConverter;

    public MentionHandler(
        ILogger<MentionHandler> logger,
        RankedLanguageIdentifier rankedLanguageIdentifier,
        WordFilter wordFilter,
        ChatService chatService,
        MarkdownConversionService markdownConversionService,
        VideoConverter videoConverter)
    {
        _logger = logger;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _wordFilter = wordFilter;
        _chatService = chatService;
        _markdownConversionService = markdownConversionService;
        _videoConverter = videoConverter;
    }

    private static TelegramFileMetadata? GetFileMetadata(Message? message)
    {
        return message switch
        {
            { Photo: { } photos } when photos.MaxBy(p => p.Width) is { } biggestPhoto => new TelegramFileMetadata(biggestPhoto.FileId, "image/jpeg", biggestPhoto.FileSize),
            { Video: { } video } => new TelegramFileMetadata(video.FileId, video.MimeType ?? "video/mp4", video.FileSize),
            { VideoNote: { } videoNote } => new TelegramFileMetadata(videoNote.FileId, "video/mp4", videoNote.FileSize),
            { Audio: { } audio } => new TelegramFileMetadata(audio.FileId, audio.MimeType ?? "audio/mpeg", audio.FileSize),
            { Voice: { } voice } => new TelegramFileMetadata(voice.FileId, voice.MimeType ?? "audio/ogg", voice.FileSize),
            { Animation: { } animation } => new TelegramFileMetadata(animation.FileId, animation.MimeType ?? "video/mp4", animation.FileSize),
            { Sticker: { } sticker } => new TelegramFileMetadata(sticker.FileId, sticker.Type == StickerType.Regular ? "image/webp" : "application/octet-stream", sticker.FileSize),
            { Document: { } document } when !string.IsNullOrEmpty(document.MimeType) => new TelegramFileMetadata(document.FileId, document.MimeType, document.FileSize),
            _ => null,
        };
    }

    private async Task<(byte[]? FileBytes, string? ErrorMessage)> DownloadMediaIfPresentAsync(
        TelegramFileMetadata? fileMetadata,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        var fileId = fileMetadata?.FileId;
        var fileSize = fileMetadata?.FileSize;

        if (string.IsNullOrEmpty(fileId))
            return (null, "No fileId provided for media download.");

        // 20 MB limit for media files
        const long maxFileSize = 20 * 1024 * 1024;

        if (fileSize is > maxFileSize)
        {
            return (null, $"File size {fileSize} bytes exceeds the maximum allowed size of {maxFileSize} bytes.");
        }

        _logger.LogDebug("Downloading media with fileId={FileId}", fileId);
        var fileInfo = await botClient.GetFile(fileId, cancellationToken);

        if (string.IsNullOrEmpty(fileInfo.FilePath))
        {
            _logger.LogWarning("FilePath is null or empty for fileId={FileId}. Cannot download media", fileId);
            return (null, "FilePath is null or empty. Cannot download media.");
        }

        try
        {
            await using var memoryStream = new MemoryStream();
            await botClient.DownloadFile(fileInfo.FilePath, memoryStream, cancellationToken);
            var downloadedImageBytes = memoryStream.ToArray();
            _logger.LogInformation("Successfully downloaded media with fileId={FileId}, size={Size} bytes", fileId, downloadedImageBytes.Length);
            return (downloadedImageBytes, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media with fileId={FileId}", fileId);
            return (null, ex.Message);
        }
    }

    private async Task<ChatResponseInfo> GetChatResponseWithRetryAsync(
        long chatId,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null)
    {
        var result = await Policy
            .Handle<ApiException>(x => x.ErrorCode is 502 or 503 or 504
                                       || x.ErrorMessage?.Contains("try again", StringComparison.OrdinalIgnoreCase) == true)
            .Or<HttpRequestException>()
            .OrResult<ChatResponseInfo>(x => string.IsNullOrWhiteSpace(x.Text) && x.DataContents.Count == 0)
            .WaitAndRetryAsync(retryCount: 2, retryNumber => TimeSpan.FromMilliseconds(retryNumber * 200))
            .ExecuteAndCaptureAsync(async () => await _chatService.GetResponseAsync(
                chatId,
                messageId,
                authorName,
                messageText,
                fileBytes,
                fileMimeType
            ));

        return result switch
        {
            { Outcome: OutcomeType.Failure, FinalException: not null } => throw result.FinalException,
            { Outcome: OutcomeType.Failure, FinalException: null } => throw new InvalidOperationException("Unexpected failure without an exception in the result."),
            _ => result.Result,
        };
    }

    public async Task HandleMentionAsync(
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
            author,
            languageCode,
            messageText,
            originalMessageLogInfo); // Log user's message and info about replied message

        var fileMetadata = GetFileMetadata(updateMessage) ?? GetFileMetadata(updateMessage.ReplyToMessage);
        _logger.LogInformation("Media info for message from {Author}: FileId={FileId}, MimeType={MimeType}",
            author,
            fileMetadata?.FileId,
            fileMetadata?.MimeType);

        var media = await DownloadMediaIfPresentAsync(fileMetadata, botClient, cancellationToken);

        const int maxFileSize = 10_000_000;
        if (fileMetadata?.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true && media.FileBytes is not null)
        {
            try
            {
                media.FileBytes = await _videoConverter.ConvertAsync(media.FileBytes, maxFileSize, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to convert video for {Author}. Error: {ErrorMessage}", author, e.Message);
                media.ErrorMessage = $"Video conversion failed: {e.Message}";
            }
        }

        if (fileMetadata?.FileId is not null && media.FileBytes is null && media.ErrorMessage is not null)
        {
            fullMessageToLlm = $"{fullMessageToLlm}\n\n[Media download error: {media.ErrorMessage}]";
        }

        var replyText = string.Empty;

        try
        {
            replyText = messageText switch
            {
                _ when _wordFilter.ContainsBannedWords(fullMessageToLlm) => "ты пидор, кстати",
                _ => (await GetChatResponseWithRetryAsync(
                        updateMessage.Chat.Id,
                        updateMessage.Id,
                        author,
                        fullMessageToLlm,
                        media.FileBytes,
                        fileMetadata?.MimeType
                     )).Text,
            };
            replyText = replyText.Cut(Const.MaxTelegramMessageLength);
            if (string.IsNullOrWhiteSpace(replyText))
            {
                replyText = "игнорирую 😏";
            }
        }
        catch (Exception e)
        {
            replyText = $"{e.GetType().Name}: {e.Message}";
        }

        var markdownReplyText = _markdownConversionService.ConvertToTelegramMarkdown(replyText);

        _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

        async Task SendReply(string text, ParseMode parseMode)
        {
            await botClient.SendMessage(
                new ChatId(updateMessage.Chat.Id),
                text,
                parseMode,
                new ReplyParameters { MessageId = updateMessage.MessageId, },
                cancellationToken: cancellationToken);
        }

        try
        {
            await SendReply(markdownReplyText, ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to send message with MarkdownV2. Falling back to plain text");
            await SendReply(replyText, ParseMode.None);
        }
    }
}
