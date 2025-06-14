using Pacos.Constants;
using Pacos.Extensions;
using Pacos.Services.GenerativeAi;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Pacos.Services.ChatCommandHandlers;

public sealed class DrawHandler
{
    private readonly ILogger<DrawHandler> _logger;
    private readonly ImageGenerationService _imageGenerationService;

    public DrawHandler(
        ILogger<DrawHandler> logger,
        ImageGenerationService imageGenerationService)
    {
        _logger = logger;
        _imageGenerationService = imageGenerationService;
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

        // Indicates image-to-image is possible
        if (sourcePhotoSizes != null)
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

            var (replyText, generatedImageData, generatedImageMime, error) = await _imageGenerationService.GenerateImageToImageAsync(prompt, imageBytes, mimeType);
            if (generatedImageData != null)
            {
                await botClient.SendPhoto(
                    chatId: updateMessage.Chat.Id,
                    photo: new InputFileStream(new MemoryStream(generatedImageData), "generated_image.png"),
                    caption: replyText?.Cut(Const.MaxTelegramCaptionLength),
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId }, // Always reply to the command message ID
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Sent image-to-image result (photo from {PhotoSourceContext}) to {Author}", photoSourceMessageContext, author);
            }
            else
            {
                await botClient.SendMessage(
                    chatId: updateMessage.Chat.Id,
                    text: !string.IsNullOrWhiteSpace(replyText) ? replyText : $"Sorry, couldn't generate image from image (photo from {photoSourceMessageContext}): {error}",
                    replyParameters: new ReplyParameters { MessageId = updateMessage.MessageId },
                    cancellationToken: cancellationToken);
                _logger.LogWarning("Failed image-to-image for {Author} (photo from {PhotoSourceContext}): {Error}", author, photoSourceMessageContext, error);
            }
        }
        else
        {
            /* Fallback to Text-to-Image if no suitable photo found */
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
}
