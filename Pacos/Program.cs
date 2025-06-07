using GenerativeAI;
using Microsoft.Extensions.AI;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NTextCat;
using Pacos.Enums;
using Pacos.Exceptions;
using Pacos.Extensions;
using Pacos.Models.Options;
using Pacos.Services;
using Pacos.Services.BackgroundTasks;
using Telegram.Bot;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Pacos;

public class Program
{
    private static readonly LoggingConfiguration LoggingConfiguration = new XmlLoggingConfiguration("nlog.config");
    private const int BackgroundTaskQueueCapacity = 100;

    public static void Main(string[] args)
    {
        // NLog: set up the logger first to catch all errors
        LogManager.Configuration = LoggingConfiguration;
        try
        {
            var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddEnvironmentVariables();
            })
            .ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                loggingBuilder.AddNLog(LoggingConfiguration);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services
                    .AddOptions<PacosOptions>()
                    .Bind(hostContext.Configuration.GetSection(nameof(OptionSections.Pacos)))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddHttpClient(nameof(HttpClientType.Telegram))
                    .AddPolicyHandler(HttpPolicyProvider.TelegramCombinedPolicy)
                    .AddDefaultLogger();

                var telegramBotApiKey = hostContext.Configuration.GetTelegramBotApiKey()
                                        ?? throw new ServiceException(LocalizationKeys.Errors.Configuration.TelegramBotApiKeyMissing);
                var googleCloudApiKey = hostContext.Configuration.GetGoogleCloudApiKey()
                                        ?? throw new ServiceException(LocalizationKeys.Errors.Configuration.GoogleCloudApiKeyMissing);

                var bannedWords = File.Exists("banwords.txt") ? File.ReadAllLines("banwords.txt") : [];

                services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
                services.AddSingleton<ITelegramBotClient>(s => new TelegramBotClient(
                        telegramBotApiKey,
                        s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpClientType.Telegram))
                    ));
                services.AddHostedService<QueuedHostedService>();
                services.AddSingleton<IChatClient>(_ => new GenerativeAI.Microsoft.GenerativeAIChatClient(googleCloudApiKey, GoogleAIModels.Gemini25ProPreview0520));
                services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(BackgroundTaskQueueCapacity));
                services.AddSingleton<RankedLanguageIdentifier>(_ => new RankedLanguageIdentifierFactory().Load("Core14.profile.xml"));
                services.AddSingleton<WordFilter>(_ => new WordFilter(bannedWords));
                services.AddSingleton<ChatService>();
                services.AddSingleton<TelegramBotService>();
                services.AddHostedService<Worker>();
            })
            .Build();

            host.Run();
        }
        catch (Exception ex)
        {
            // NLog: catch setup errors
            LogManager.GetCurrentClassLogger().Error(ex, "Stopped program because of exception");
            throw;
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }
}
