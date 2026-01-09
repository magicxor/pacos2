using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Models;
using Pacos.Services.GenerativeAi;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class DrawHandler
{
    private readonly ILogger<DrawHandler> _logger;
    private readonly ImageGenerationService _imageGenerationService;
    private readonly TelegramMediaService _telegramMediaService;

    public DrawHandler(
        ILogger<DrawHandler> logger,
        ImageGenerationService imageGenerationService,
        TelegramMediaService telegramMediaService)
    {
        _logger = logger;
        _imageGenerationService = imageGenerationService;
        _telegramMediaService = telegramMediaService;
    }

    public async Task HandleDrawAsync(
        ITelegramBotClient botClient,
        Message updateMessage,
        string messageText,
        string author,
        CancellationToken cancellationToken)
    {
        var prompt = messageText.Substring(Const.DrawCommand.Length).Trim();
        _logger.LogInformation("Processing {Command} command from {Author} with prompt: {Prompt}", Const.DrawCommand, author, prompt);

        TelegramFileMetadata? sourceFileMetadata = null;
        string mediaSourceContext = "current command message";

        // 1. Check current message for photo or sticker
        var currentMessageMetadata = GetImageMetadata(updateMessage);
        if (currentMessageMetadata != null)
        {
            sourceFileMetadata = currentMessageMetadata;
        }
        // 2. If no media in current message, AND it's a reply, check replied-to message
        else if (updateMessage.ReplyToMessage != null)
        {
            var replyMetadata = GetImageMetadata(updateMessage.ReplyToMessage);
            if (replyMetadata != null)
            {
                sourceFileMetadata = replyMetadata;
                mediaSourceContext = "replied-to message";
                _logger.LogInformation("No image in !draw command by {Author}, attempting to use image from replied-to message", author);
            }
        }

        // Indicates image-to-image is possible
        if (sourceFileMetadata != null)
        {
            // Image-to-Image
            _logger.LogInformation("Performing image-to-image for {Author} using media from {MediaSourceContext}", author, mediaSourceContext);

            var (imageBytes, downloadError) = await _telegramMediaService.DownloadMediaAsync(sourceFileMetadata, botClient, cancellationToken);
            if (imageBytes == null)
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: $"Failed to download image: {downloadError}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                return;
            }

            var (replyText, generatedImageData, generatedImageMime, error) = await _imageGenerationService.GenerateImageToImageAsync(prompt, imageBytes, sourceFileMetadata.MimeType);
            if (generatedImageData != null)
            {
                await botClient.SendPhoto(
                    chatId: updateMessage.Chat.Id,
                    photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                    caption: replyText?.Cut(Const.MaxTelegramCaptionLength),
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId }, // Always reply to the command message ID
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Sent image-to-image result (media from {MediaSourceContext}) to {Author}", mediaSourceContext, author);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: !string.IsNullOrWhiteSpace(replyText) ? replyText : $"Sorry, couldn't generate image from image (media from {mediaSourceContext}): {error}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogWarning("Failed image-to-image for {Author} (media from {MediaSourceContext}): {Error}", author, mediaSourceContext, error);
            }
        }
        else
        {
            /* Fallback to Text-to-Image if no suitable image found */
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

            var (replyText, generatedImageData, generatedImageMime, error) = await _imageGenerationService.GenerateTextToImageAsync(prompt);
            if (generatedImageData != null)
            {
                await botClient.SendPhoto(
                    chatId: updateMessage.Chat.Id,
                    photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                    caption: replyText?.Cut(Const.MaxTelegramCaptionLength),
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Sent text-to-image result to {Author}", author);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: !string.IsNullOrWhiteSpace(replyText) ? replyText : $"Sorry, couldn't generate image from text: {error}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogWarning("Failed text-to-image for {Author}: {Error}", author, error);
            }
        }
    }

    /// <summary>
    /// Gets image metadata from a message (Photo or Sticker only).
    /// Returns null for other media types.
    /// </summary>
    private static TelegramFileMetadata? GetImageMetadata(Message message)
    {
        var metadata = TelegramMediaService.GetFileMetadata(message);
        return metadata?.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            ? metadata
            : null;
    }
}
