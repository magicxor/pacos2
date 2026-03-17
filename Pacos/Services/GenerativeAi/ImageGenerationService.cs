using GenerativeAI;
using GenerativeAI.Exceptions;
using GenerativeAI.Types;
using Microsoft.Extensions.Options;
using Pacos.Constants;
using Pacos.Enums;
using Pacos.Models.Options;

namespace Pacos.Services.GenerativeAi;

public sealed class ImageGenerationService
{
    private readonly IOptions<PacosOptions> _options;
    private readonly ILogger<ImageGenerationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ImageGenerationService(
        IOptions<PacosOptions> options,
        ILogger<ImageGenerationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private GenerativeModel CreateGenerativeModel(string model)
    {
        return new GenerativeModel(
            apiKey: _options.Value.GoogleCloudApiKey,
            model: model,
            new GenerationConfig { ResponseModalities = [Modality.IMAGE, Modality.TEXT] },
            Const.SafetySettings,
            httpClient: _httpClientFactory.CreateClient(nameof(HttpClientType.GoogleCloud)),
            logger: _logger);
    }

    private static bool IsModelUnavailableException(Exception ex)
    {
        return ex is ApiException { ErrorCode: 503 };
    }

    private string[] GetModelsWithFallback()
    {
        var primary = _options.Value.ImageGenerationModel;
        var fallback = _options.Value.ImageGenerationModelFallback;
        return !string.IsNullOrWhiteSpace(fallback)
            ? [primary, primary, fallback]
            : [primary, primary];
    }

    public async Task<(string? text, byte[]? imageData, string? mimeType, string? errorMessage)> GenerateTextToImageAsync(string prompt)
    {
        var models = GetModelsWithFallback();

        for (var attempt = 0; attempt < models.Length; attempt++)
        {
            try
            {
                _logger.LogInformation("Attempting text-to-image generation (attempt {Attempt}, model {Model}) for prompt: {Prompt}", attempt + 1, models[attempt], prompt);

                var fullPrompt = $"Generate an image of: {prompt}";

                var generativeModel = CreateGenerativeModel(models[attempt]);
                var response = await generativeModel.GenerateContentAsync(fullPrompt);

                if (response.Candidates is { Length: > 0 })
                {
                    var candidate = response.Candidates.First();
                    if (candidate.Content is { Parts.Count: > 0 })
                    {
                        var imagePart = candidate.Content.Parts.FirstOrDefault(p => p.InlineData != null && (p.InlineData.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true));
                        if (imagePart != null)
                        {
                            _logger.LogInformation("Successfully generated image from text prompt: {Prompt}", prompt);
                            return (response.Text, Convert.FromBase64String(imagePart.InlineData?.Data ?? string.Empty), imagePart.InlineData?.MimeType, null);
                        }

                        _logger.LogWarning("No image part found in response for text prompt: {Prompt}. Response text: {Text}", prompt, response.Text);
                    }
                }

                return (response.Text, null, null, "Could not extract image from the response. The model might not have generated one.");
            }
            catch (Exception ex) when (IsModelUnavailableException(ex) && attempt < models.Length - 1)
            {
                _logger.LogWarning(ex, "Model {Model} unavailable for text-to-image (attempt {Attempt}), will retry", models[attempt], attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during text-to-image generation for prompt: {Prompt}", prompt);
                return (null, null, null, $"An error occurred while generating the image: {ex.Message}");
            }
        }

        return (null, null, null, "An error occurred while generating the image.");
    }

    public async Task<(string? text, byte[]? imageData, string? mimeType, string? errorMessage)> GenerateImageToImageAsync(string prompt, byte[] inputImageBytes, string mimeType)
    {
        var models = GetModelsWithFallback();

        for (var attempt = 0; attempt < models.Length; attempt++)
        {
            try
            {
                _logger.LogInformation("Attempting image-to-image generation (attempt {Attempt}, model {Model}) for prompt: {Prompt}", attempt + 1, models[attempt], prompt);

                var imagePartContent = new Part
                {
                    InlineData = new Blob
                    {
                        Data = Convert.ToBase64String(inputImageBytes),
                        MimeType = mimeType,
                    },
                };
                var textPartContent = new Part(prompt);

                var contentParts = new List<Part> { imagePartContent, textPartContent };

                var generativeModel = CreateGenerativeModel(models[attempt]);
                var response = await generativeModel.GenerateContentAsync(contentParts.ToArray());

                if (response.Candidates is { Length: > 0 })
                {
                    var candidate = response.Candidates.First();
                    if (candidate.Content is { Parts.Count: > 0 })
                    {
                        var outputImagePart = candidate.Content.Parts.FirstOrDefault(p => p.InlineData != null && (p.InlineData.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true));
                        if (outputImagePart != null)
                        {
                            _logger.LogInformation("Successfully generated image from image input with prompt: {Prompt}", prompt);
                            return (response.Text, Convert.FromBase64String(outputImagePart.InlineData?.Data ?? string.Empty), outputImagePart.InlineData?.MimeType, null);
                        }

                        _logger.LogWarning("No output image part found in image-to-image response for prompt: {Prompt}. Response text: {Text}", prompt, response.Text);
                    }
                }

                return (response.Text, null, null, "Could not extract modified image from the response. The model might not have generated one.");
            }
            catch (Exception ex) when (IsModelUnavailableException(ex) && attempt < models.Length - 1)
            {
                _logger.LogWarning(ex, "Model {Model} unavailable for image-to-image (attempt {Attempt}), will retry", models[attempt], attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during image-to-image generation for prompt: {Prompt}", prompt);
                return (null, null, null, $"An error occurred while processing the image: {ex.Message}");
            }
        }

        return (null, null, null, "An error occurred while processing the image.");
    }
}
