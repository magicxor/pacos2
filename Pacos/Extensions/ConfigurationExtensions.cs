using Pacos.Enums;
using Pacos.Models.Options;

namespace Pacos.Extensions;

public static class ConfigurationExtensions
{
    public static string? GetImageGenerationModel(this IConfiguration configuration)
    {
        return configuration.GetSection(nameof(OptionSections.Pacos)).GetValue<string>(nameof(PacosOptions.ImageGenerationModel));
    }

    public static string? GetGoogleCloudApiKey(this IConfiguration configuration)
    {
        return configuration.GetSection(nameof(OptionSections.Pacos)).GetValue<string>(nameof(PacosOptions.GoogleCloudApiKey));
    }

    public static string? GetApiVersion(this IConfiguration configuration)
    {
        return configuration.GetSection(nameof(OptionSections.Pacos)).GetValue<string>(nameof(PacosOptions.ApiVersion));
    }
}
