using Microsoft.Extensions.Options;
using Pacos.Models.Options;
using GenerativeAI;
using GenerativeAI.Types;

namespace Pacos.Services;

public class GenerativeModelService
{
    private readonly IOptions<PacosOptions> _options;
    private readonly ILogger<GenerativeModelService> _logger;

    private static List<SafetySetting> GetImgSafetySettings()
    {
        return
        [
            new()
            {
                Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH,
                Threshold = HarmBlockThreshold.OFF,
            },

            new()
            {
                Category = HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT,
                Threshold = HarmBlockThreshold.OFF,
            },

            new()
            {
                Category = HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT,
                Threshold = HarmBlockThreshold.OFF,
            },

            new()
            {
                Category = HarmCategory.HARM_CATEGORY_HARASSMENT,
                Threshold = HarmBlockThreshold.OFF,
            },

            new()
            {
                Category = HarmCategory.HARM_CATEGORY_CIVIC_INTEGRITY,
                Threshold = HarmBlockThreshold.OFF,
            },

        ];
    }

    public GenerativeModelService(
        IOptions<PacosOptions> options,
        ILogger<GenerativeModelService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<(byte[]? imageData, string? mimeType, string? errorMessage)> GenerateTextToImageAsync(string prompt)
    {
        try
        {
            _logger.LogInformation("Attempting text-to-image generation for prompt: {Prompt}", prompt);

            var fullPrompt = $"Generate an image of: {prompt}";
            var generativeModel = new GenerativeModel(
                apiKey: _options.Value.GoogleCloudApiKey,
                model: _options.Value.ImageGenerationModel,
                new GenerationConfig { ResponseModalities = [Modality.IMAGE, Modality.TEXT] },
                GetImgSafetySettings());
            var response = await generativeModel.GenerateContentAsync(fullPrompt);

            if (response.Candidates is { Length: > 0 })
            {
                var candidate = response.Candidates.First();
                if (candidate.Content is { Parts.Count: > 0 })
                {
                    // Look for an image part in the response
                    var imagePart = candidate.Content.Parts.FirstOrDefault(p => p.InlineData != null && (p.InlineData.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true));
                    if (imagePart != null)
                    {
                        _logger.LogInformation("Successfully generated image from text prompt: {Prompt}", prompt);
                        return (Convert.FromBase64String(imagePart.InlineData?.Data ?? string.Empty), imagePart.InlineData?.MimeType, null);
                    }

                    _logger.LogWarning("No image part found in response for text prompt: {Prompt}. Response text: {Text}", prompt, response.Text);
                }
            }

            return (null, null, "Could not extract image from the response. The model might not have generated one.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during text-to-image generation for prompt: {Prompt}", prompt);
            return (null, null, $"An error occurred while generating the image: {ex.Message}");
        }
    }

    public async Task<(byte[]? imageData, string? mimeType, string? errorMessage)> GenerateImageToImageAsync(string prompt, byte[] inputImageBytes, string mimeType)
    {
        try
        {
            _logger.LogInformation("Attempting image-to-image generation for prompt: {Prompt}", prompt);

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

            var generativeModel = new GenerativeModel(
                apiKey: _options.Value.GoogleCloudApiKey,
                model: _options.Value.ImageGenerationModel,
                new GenerationConfig { ResponseModalities = [Modality.IMAGE, Modality.TEXT] },
                GetImgSafetySettings());
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
                        return (Convert.FromBase64String(outputImagePart.InlineData?.Data ?? string.Empty), outputImagePart.InlineData?.MimeType, null);
                    }

                    _logger.LogWarning("No output image part found in image-to-image response for prompt: {Prompt}. Response text: {Text}", prompt, response.Text);
                }
            }

            return (null, null, "Could not extract modified image from the response. The model might not have generated one.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image-to-image generation for prompt: {Prompt}", prompt);
            return (null, null, $"An error occurred while processing the image: {ex.Message}");
        }
    }
}
