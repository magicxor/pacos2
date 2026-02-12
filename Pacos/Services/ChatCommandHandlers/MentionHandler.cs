using GenerativeAI.Exceptions;
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
    private readonly ChatService _chatService;
    private readonly MarkdownConversionService _markdownConversionService;
    private readonly VideoConverter _videoConverter;
    private readonly TelegramMediaService _telegramMediaService;

    public MentionHandler(
        ILogger<MentionHandler> logger,
        RankedLanguageIdentifier rankedLanguageIdentifier,
        ChatService chatService,
        MarkdownConversionService markdownConversionService,
        VideoConverter videoConverter,
        TelegramMediaService telegramMediaService)
    {
        _logger = logger;
        _rankedLanguageIdentifier = rankedLanguageIdentifier;
        _chatService = chatService;
        _markdownConversionService = markdownConversionService;
        _videoConverter = videoConverter;
        _telegramMediaService = telegramMediaService;
    }

    private async Task<ChatResponseInfo> GetChatResponseWithRetryAsync(
        long chatId,
        bool isGroupChat,
        long messageId,
        string authorName,
        string messageText,
        byte[]? fileBytes = null,
        string? fileMimeType = null)
    {
        return await Policy
            .Handle<ApiException>(x => x.ErrorCode is 502 or 503 or 504
                                       || x.ErrorMessage?.Contains("try again", StringComparison.OrdinalIgnoreCase) == true)
            .Or<HttpRequestException>()
            .OrResult<ChatResponseInfo>(x => string.IsNullOrWhiteSpace(x.Text) && x.DataContents.Count == 0)
            .WaitAndRetryAsync(retryCount: 2, retryNumber => TimeSpan.FromMilliseconds(retryNumber * 200))
            .ExecuteAsync(async () => await _chatService.GetResponseAsync(
                chatId,
                isGroupChat,
                messageId,
                authorName,
                messageText,
                fileBytes,
                fileMimeType
            ));
    }

    public async Task HandleMentionAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        bool isGroupChat,
        string author,
        string currentMention,
        CancellationToken cancellationToken)
    {
        // Remove the mention from the message
        messageText = messageText.Substring(currentMention.Length).TrimStart(',', ' ', '.', '!', '?', ':', ';').Trim();

        // Check for replied-to message text
        var repliedToMessageText = (updateMessage.ReplyToMessage?.Text ?? updateMessage.ReplyToMessage?.Caption ?? string.Empty).Trim();

        // Only exit early if both message and replied-to message are empty
        if (string.IsNullOrEmpty(messageText) && string.IsNullOrEmpty(repliedToMessageText))
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
            repliedToMessageText = (updateMessage.ReplyToMessage.Text ?? updateMessage.ReplyToMessage.Caption ?? string.Empty).Trim();
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
            originalMessageLogInfo);

        var fileMetadata = TelegramMediaService.GetFileMetadata(updateMessage) ?? TelegramMediaService.GetFileMetadata(updateMessage.ReplyToMessage);
        _logger.LogInformation("Media info for message from {Author}: FileId={FileId}, MimeType={MimeType}",
            author,
            fileMetadata?.FileId,
            fileMetadata?.MimeType);

        var media = await _telegramMediaService.DownloadMediaAsync(fileMetadata, botClient, cancellationToken);

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

        string replyText;

        try
        {
            replyText = (await GetChatResponseWithRetryAsync(
                updateMessage.Chat.Id,
                isGroupChat,
                updateMessage.Id,
                author,
                fullMessageToLlm,
                media.FileBytes,
                fileMetadata?.MimeType
            )).Text;

            replyText = replyText.Cut(Const.MaxTelegramMessageLength);

            if (string.IsNullOrWhiteSpace(replyText))
            {
                replyText = "Error: Received empty response from chat service.";
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get chat response for {Author}", author);
            replyText = $"{e.GetType().Name}: {e.Message}";
        }

        var markdownReplyText = _markdownConversionService.ConvertToTelegramMarkdown(replyText);

        _logger.LogInformation("Replying to {Author} with: {ReplyText}", author, replyText);

        try
        {
            await SendReply(markdownReplyText, ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to send message with MarkdownV2. Falling back to plain text");
            await SendReply(replyText, ParseMode.None);
        }

        return;

        async Task SendReply(string text, ParseMode parseMode)
        {
            await botClient.SendMessage(
                new ChatId(updateMessage.Chat.Id),
                text,
                parseMode,
                new ReplyParameters { MessageId = updateMessage.MessageId, },
                cancellationToken: cancellationToken);
        }
    }
}
