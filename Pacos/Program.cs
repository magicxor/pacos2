using System.Net;
using GenerativeAI;
using GenerativeAI.Microsoft;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NTextCat;
using Pacos.Constants;
using Pacos.Enums;
using Pacos.Models.Options;
using Pacos.Services;
using Pacos.Services.BackgroundTasks;
using Pacos.Services.ChatCommandHandlers;
using Pacos.Services.GenerativeAi;
using Pacos.Services.Markdown;
using Pacos.Utils;
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
                .ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddEnvironmentVariables())
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

                    var telegramRequestTimeout = TimeSpan.FromSeconds(40);
                    services.AddHttpClient(nameof(HttpClientType.Telegram), httpClient => httpClient.Timeout = telegramRequestTimeout)
                        .AddDefaultLogger()
                        .AddStandardResilienceHandler(x =>
                        {
                            x.AttemptTimeout = new HttpTimeoutStrategyOptions { Timeout = telegramRequestTimeout };
                            x.TotalRequestTimeout = new HttpTimeoutStrategyOptions { Timeout = x.AttemptTimeout.Timeout * 2 };
                            x.CircuitBreaker.SamplingDuration = x.AttemptTimeout.Timeout * 2;
                        });

                    var googleRequestTimeout = TimeSpan.FromMinutes(2);
                    services.AddHttpClient(nameof(HttpClientType.GoogleCloud), httpClient => httpClient.Timeout = googleRequestTimeout)
                        .ConfigurePrimaryHttpMessageHandler((handler, serviceProvider) =>
                        {
                            var proxyAddress = serviceProvider.GetRequiredService<IOptions<PacosOptions>>().Value.WebProxy;
                            var proxyUsername = serviceProvider.GetRequiredService<IOptions<PacosOptions>>().Value.WebProxyLogin;
                            var proxyPassword = serviceProvider.GetRequiredService<IOptions<PacosOptions>>().Value.WebProxyPassword;

                            var webProxy = string.IsNullOrWhiteSpace(proxyAddress)
                                ? null
                                : new WebProxy(
                                    Address: proxyAddress,
                                    BypassOnLocal: true,
                                    BypassList: null,
                                    Credentials: new NetworkCredential(proxyUsername, proxyPassword));

                            switch (handler)
                            {
                                case SocketsHttpHandler socketsHttpHandler:
                                    socketsHttpHandler.Proxy = webProxy;
                                    break;
                                case HttpClientHandler httpClientHandler:
                                    httpClientHandler.Proxy = webProxy;
                                    break;
                                default:
                                    serviceProvider.GetService<ILogger<IHttpClientBuilder>>()?.LogWarning(
                                        "Unknown HttpMessageHandler type: {HandlerType}. Proxy will not be set",
                                        handler.GetType().FullName);
                                    break;
                            }
                        })
                        .AddDefaultLogger()
                        .AddStandardResilienceHandler(x =>
                        {
                            x.AttemptTimeout = new HttpTimeoutStrategyOptions { Timeout = googleRequestTimeout };
                            x.TotalRequestTimeout = new HttpTimeoutStrategyOptions { Timeout = x.AttemptTimeout.Timeout * 2 };
                            x.CircuitBreaker.SamplingDuration = x.AttemptTimeout.Timeout * 2;
                        });

                    var bannedWords = File.Exists(BanWordsFileName)
                        ? File.ReadAllLines(BanWordsFileName)
                        : [];

                    services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
                    services.AddSingleton<ITelegramBotClient>(s => new TelegramBotClient(
                            s.GetRequiredService<IOptions<PacosOptions>>().Value.TelegramBotApiKey,
                            s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpClientType.Telegram))
                        ));
                    services.AddHostedService<QueuedHostedService>();
                    services.AddSingleton<MarkdownConversionService>();
                    services.AddSingleton<IChatClient>(s =>
                    {
                        var chatGenerativeModel = new GenerativeModel(
                            apiKey: s.GetRequiredService<IOptions<PacosOptions>>().Value.GoogleCloudApiKey,
                            model: s.GetRequiredService<IOptions<PacosOptions>>().Value.ChatModel,
                            safetySettings: Const.SafetySettings,
                            httpClient: s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpClientType.GoogleCloud)),
                            logger: s.GetRequiredService<ILogger<GenerativeModel>>());

                        var chatClientObj = new GenerativeAIChatClient(
                            adapter: chatGenerativeModel.Platform,
                            modelName: s.GetRequiredService<IOptions<PacosOptions>>().Value.ChatModel);

                        chatClientObj.model.EnableFunctions();
                        chatClientObj.model.UseGoogleSearch = true;
                        chatClientObj.AutoCallFunction = true;

                        chatClientObj.ReplaceModel(chatGenerativeModel, s.GetRequiredService<ILogger<IChatClient>>());

                        return chatClientObj;
                    });
                    services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(BackgroundTaskQueueCapacity));
                    services.AddSingleton<RankedLanguageIdentifier>(_ => new RankedLanguageIdentifierFactory().Load(RankedLanguageIdentifierFileName));
                    services.AddSingleton<WordFilter>(_ => new WordFilter(bannedWords));
                    services.AddSingleton<ChatService>();
                    services.AddSingleton<ImageGenerationService>();
                    services.AddSingleton<DrawHandler>();
                    services.AddSingleton<ResetHandler>();
                    services.AddSingleton<MentionHandler>();
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
