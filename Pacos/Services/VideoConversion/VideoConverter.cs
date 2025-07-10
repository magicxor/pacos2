using FFMpegCore;
using FFMpegCore.Enums;

namespace Pacos.Services.VideoConversion;

public class VideoConverter
{
    private readonly ILogger<VideoConverter> _logger;

    public VideoConverter(
        ILogger<VideoConverter> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ConvertAsync(byte[] fileBytes, long fileSizeLimit, CancellationToken cancellationToken)
    {
        if (fileBytes.Length <= fileSizeLimit)
        {
            _logger.LogInformation("File size is within the limit. Returning original file bytes");
            return fileBytes;
        }

        var tempInputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");

        try
        {
            _logger.LogInformation("Writing input bytes to temporary file: {TempInputPath}", tempInputPath);
            await File.WriteAllBytesAsync(tempInputPath, fileBytes, cancellationToken);

            var mediaInfo = await FFProbe.AnalyseAsync(tempInputPath, cancellationToken: cancellationToken);

            const int maxWidth = 1280;
            const int maxHeight = 720;
            var convertedFileSizeLimit = fileSizeLimit * 0.9;
            const long maxAudioBitrate = 64000; // 64 kbps

            var videoStream = mediaInfo.PrimaryVideoStream ?? throw new InvalidOperationException("No video stream found in the file.");

            var ratioX = (double)maxWidth / videoStream.Width;
            var ratioY = (double)maxHeight / videoStream.Height;
            var ratio = Math.Min(ratioX, ratioY);

            // Don't upscale video if it's already smaller than maximum dimensions
            int newWidth, newHeight;
            if (ratio > 1.0)
            {
                // Use original dimensions since video is already smaller than maximum
                newWidth = videoStream.Width;
                newHeight = videoStream.Height;
            }
            else
            {
                // Downscale video to maximum dimensions
                newWidth = (int)(videoStream.Width * ratio);
                newHeight = (int)(videoStream.Height * ratio);
            }

            if (newWidth % 2 != 0) newWidth--;
            if (newHeight % 2 != 0) newHeight--;

            var ffmpeg = FFMpegArguments.FromFileInput(tempInputPath);

            if (mediaInfo.PrimaryAudioStream == null)
            {
                _logger.LogInformation("No audio stream detected. Adding a silent audio track");

                long calculatedVideoBitrate = (long)(convertedFileSizeLimit * 8 / mediaInfo.Duration.TotalSeconds);
                var newVideoBitrate = Math.Min(videoStream.BitRate, calculatedVideoBitrate);
                if (newVideoBitrate < 100000) newVideoBitrate = 100000;

                _logger.LogInformation("New video parameters (no audio): Width={NewWidth}, Height={NewHeight}, VideoBitrate={NewVideoBitrate}", newWidth, newHeight, newVideoBitrate);

                await ffmpeg.OutputToFile(tempOutputPath, false, options => options
                        .WithCustomArgument("-f lavfi -i anullsrc")
                        .WithVideoFilters(x => x.Scale(newWidth, newHeight))
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithVideoBitrate(Convert.ToInt32((double)newVideoBitrate / 1000))
                        .WithSpeedPreset(Speed.Faster)
                        .WithCustomArgument("-map 0:v") // Map video from the first input.
                        .WithCustomArgument("-map 1:a") // Map audio from the second input (anullsrc).
                        .WithAudioCodec("libopus")
                        .WithAudioBitrate(16)
                        .WithCustomArgument("-shortest") // Set output duration to the shortest input.
                        .WithFastStart()
                        .ForceFormat("mp4"))
                    .ProcessAsynchronously();
            }
            else
            {
                var audioStream = mediaInfo.PrimaryAudioStream;
                var newAudioBitrate = Math.Min(audioStream.BitRate, maxAudioBitrate);

                long calculatedVideoBitrate = (long)(convertedFileSizeLimit * 8 / mediaInfo.Duration.TotalSeconds) - newAudioBitrate;
                var newVideoBitrate = Math.Min(videoStream.BitRate, calculatedVideoBitrate);
                if (newVideoBitrate < 100000) newVideoBitrate = 100000;

                _logger.LogInformation("New video parameters: Width={NewWidth}, Height={NewHeight}, VideoBitrate={NewVideoBitrate}, AudioBitrate={NewAudioBitrate}", newWidth, newHeight, newVideoBitrate, newAudioBitrate);

                await ffmpeg.OutputToFile(tempOutputPath, false, options => options
                        .WithVideoFilters(x => x.Scale(newWidth, newHeight))
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithVideoBitrate(Convert.ToInt32((double)newVideoBitrate / 1000))
                        .WithSpeedPreset(Speed.Faster)
                        .WithAudioCodec("libopus")
                        .WithAudioBitrate(Convert.ToInt32((double)newAudioBitrate / 1000))
                        .WithFastStart()
                        .ForceFormat("mp4"))
                    .ProcessAsynchronously();
            }

            _logger.LogInformation("Video conversion completed. Reading output file: {TempOutputPath}", tempOutputPath);

            var outputBytes = await File.ReadAllBytesAsync(tempOutputPath, cancellationToken);
            _logger.LogInformation("Output file size: {OutputSize} bytes", outputBytes.Length);
            return outputBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during video conversion");
            throw;
        }
        finally
        {
            if (File.Exists(tempInputPath)) File.Delete(tempInputPath);
            if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath);
            _logger.LogInformation("Temporary files cleaned up");
        }
    }
}
