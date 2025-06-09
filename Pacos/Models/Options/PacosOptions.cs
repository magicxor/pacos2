using System.ComponentModel.DataAnnotations;

namespace Pacos.Models.Options;

public class PacosOptions
{
    [Required]
    [RegularExpression(@".*:.*")]
    public required string TelegramBotApiKey { get; init; }

    [Required]
    public required string GoogleCloudApiKey { get; set; }

    [Required]
    [MinLength(1)]
    public required long[] AllowedChatIds { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] AllowedLanguageCodes { get; set; }
}
