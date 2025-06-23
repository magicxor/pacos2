using System.ComponentModel.DataAnnotations;

namespace Pacos.Models.Options;

public sealed class PacosOptions
{
    [Required]
    [RegularExpression(".*:.*")]
    public required string TelegramBotApiKey { get; init; }

    [Required]
    public required string GoogleCloudApiKey { get; set; }

    [Required]
    [MinLength(1)]
    public required long[] AllowedChatIds { get; set; }

    [Required]
    public required string ChatModel { get; set; }

    [Required]
    public required string ImageGenerationModel { get; set; }

    public required string? WebProxy { get; set; }

    public required string? WebProxyLogin { get; set; }

    public required string? WebProxyPassword { get; set; }
}
