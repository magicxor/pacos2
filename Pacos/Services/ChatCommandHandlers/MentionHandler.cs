using Microsoft.Extensions.Options;
using NTextCat;
using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models.Options;
using Pacos.Services.GenerativeAi;
using Pacos.Services.Markdown;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class MentionHandler
{
    private readonly ILogger<MentionHandler> _logger;
    private readonly IOptions<PacosOptions> _options;
    private readonly RankedLanguageIdentifier _rankedLanguageIdentifier;
    private readonly WordFilter _wordFilter;
    private readonly ChatService _chatService;
    private readonly MarkdownConversionService _markdownConversionService;

    public MentionHandler(
        ILogger<MentionHandler> logger,
        IOptions<PacosOptions> options,
        RankedLanguageIdentifier rankedLanguageIdentifier,
        WordFilter wordFilter,
        ChatService chatService,
        MarkdownConversionService markdownConversionService)
    {
        _logger = logger;
        _options = options;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _wordFilter = wordFilter;
        _chatService = chatService;
        _markdownConversionService = markdownConversionService;
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
            { Document: { } document } when !string.IsNullOrEmpty(document.MimeType) => (document.FileId, document.MimeType),
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

        var mediaInfo = GetFileInfo(updateMessage) ?? GetFileInfo(updateMessage.ReplyToMessage);
        _logger.LogInformation("Media info for message from {Author}: FileId={FileId}, MimeType={MimeType}",
            author,
            mediaInfo?.FileId,
            mediaInfo?.MimeType);

        var mediaBytes = await DownloadMediaIfPresentAsync(mediaInfo?.FileId, botClient, cancellationToken);

        var replyText = string.Empty;

        try
        {
            replyText = messageText switch
            {
                _ when _wordFilter.ContainsBannedWords(fullMessageToLlm) => "ты пидор, кстати",
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
            if (string.IsNullOrWhiteSpace(replyText))
            {
                replyText = "игнорирую 😏";
            }
        }
        catch (Exception e)
        {
            replyText = $"{e.GetType().Name}: {e.Message}";
        }

        replyText = _markdownConversionService.ConvertToTelegramMarkdown(replyText);

        _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

        async Task SendReply(ParseMode parseMode)
        {
            await botClient.SendMessage(new ChatId(updateMessage.Chat.Id),
                replyText,
                parseMode: parseMode,
                replyParameters: new ReplyParameters
                {
                    MessageId = updateMessage.MessageId,
                },
                cancellationToken: cancellationToken);
        }

        try
        {
            await SendReply(ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to send message with MarkdownV2. Falling back to plain text");
            await SendReply(ParseMode.None);
        }
    }
}
