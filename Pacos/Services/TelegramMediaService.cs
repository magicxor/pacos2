using Pacos.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Pacos.Services;

public sealed class TelegramMediaService
{
    private readonly ILogger<TelegramMediaService> _logger;

    public TelegramMediaService(ILogger<TelegramMediaService> logger)
    {
        _logger = logger;
    }

    public static TelegramFileMetadata? GetFileMetadata(Message? message)
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

    public async Task<(byte[]? FileBytes, string? ErrorMessage)> DownloadMediaAsync(
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
            var downloadedBytes = memoryStream.ToArray();
            _logger.LogInformation("Successfully downloaded media with fileId={FileId}, size={Size} bytes", fileId, downloadedBytes.Length);
            return (downloadedBytes, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media with fileId={FileId}", fileId);
            return (null, ex.Message);
        }
    }
}
