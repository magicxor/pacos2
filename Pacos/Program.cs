using GenerativeAI;
using GenerativeAI.Microsoft;
using GenerativeAI.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NTextCat;
using Pacos.Enums;
using Pacos.Extensions;
using Pacos.Models.Options;
using Pacos.Services;
using Pacos.Services.BackgroundTasks;
using Telegram.Bot;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Pacos;

public sealed class Program
{
    private const string NLogConfigFileName = "nlog.config";
    private const string BanWordsFileName = "banwords.txt";
    private const string RankedLanguageIdentifierFileName = "Core14.profile.xml";
    private const int BackgroundTaskQueueCapacity = 100;

    private static readonly LoggingConfiguration LoggingConfiguration = new XmlLoggingConfiguration(NLogConfigFileName);

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
                    .AddDefaultLogger()
                    .AddStandardResilienceHandler();

                var bannedWords = File.Exists(BanWordsFileName)
                    ? File.ReadAllLines(BanWordsFileName)
                    : [];
                var imageGenerationModel = hostContext.Configuration.GetImageGenerationModel() ?? GoogleAIModels.Gemini2FlashExpImageGeneration;
                var googleCloudApiKey = hostContext.Configuration.GetGoogleCloudApiKey() ?? throw new InvalidOperationException("Google Cloud API key is not configured.");
                var apiVersion = hostContext.Configuration.GetApiVersion() ?? ApiVersions.v1;

                services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
                services.AddSingleton<ITelegramBotClient>(s => new TelegramBotClient(
                        s.GetRequiredService<IOptions<PacosOptions>>().Value.TelegramBotApiKey,
                        s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpClientType.Telegram))
                    ));
                services.AddHostedService<QueuedHostedService>();
                services.AddSingleton<MarkdownConversionService>();
                services.AddGenerativeAI(x =>
                {
                    x.ApiVersion = apiVersion;
                    x.Model = imageGenerationModel;
                    x.Credentials = new GoogleAICredentials(googleCloudApiKey);
                });
                services.AddSingleton<IChatClient>(s =>
                {
                    var loggerFactory = s.GetRequiredService<ILoggerFactory>();
                    var chatClientObj = new GenerativeAIChatClient(
                            s.GetRequiredService<IOptions<PacosOptions>>().Value.GoogleCloudApiKey,
                            s.GetRequiredService<IOptions<PacosOptions>>().Value.ChatModel,
                            autoCallFunction: true);

                    chatClientObj.model.EnableFunctions();
                    chatClientObj.model.UseGoogleSearch = true;

                    var chatClient = chatClientObj
                        .AsBuilder()
                        .UseFunctionInvocation(loggerFactory, cfg => cfg.AllowConcurrentInvocation = true)
                        .UseLogging(loggerFactory)
                        .Build();
                    return chatClient;
                });
                services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(BackgroundTaskQueueCapacity));
                services.AddSingleton<RankedLanguageIdentifier>(_ => new RankedLanguageIdentifierFactory().Load(RankedLanguageIdentifierFileName));
                services.AddSingleton<WordFilter>(_ => new WordFilter(bannedWords));
                services.AddSingleton<ChatService>();
                services.AddSingleton<GenerativeModelService>();
                services.AddSingleton<TelegramBotService>();
                services.AddHostedService<Worker>();
            })
            .Build();

            host.Run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // NLog: catch setup errors
            LogManager.GetCurrentClassLogger().Error(ex, "Stopped program because of exception");
            throw;
        }
        catch (OperationCanceledException)
        {
            // This is expected when the application is shutting down gracefully
#pragma warning disable S6667
            LogManager.GetCurrentClassLogger().Info("Application shut down gracefully.");
#pragma warning restore S6667
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }
}
