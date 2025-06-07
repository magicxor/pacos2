using Pacos.Enums;
using Pacos.Models.Options;

namespace Pacos.Extensions;

public static class ConfigurationExtensions
{
    public static string? GetTelegramBotApiKey(this IConfiguration configuration)
    {
        return configuration.GetSection(nameof(OptionSections.Pacos)).GetValue<string>(nameof(PacosOptions.TelegramBotApiKey));
    }

    public static string? GetGoogleCloudApiKey(this IConfiguration configuration)
    {
        return configuration.GetSection(nameof(OptionSections.Pacos)).GetValue<string>(nameof(PacosOptions.GoogleCloudApiKey));
    }
}
