using Pacos.Services;

namespace Pacos;

public class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private TelegramBotService? _telegramBotService;

    public Worker(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    private static readonly TimeSpan DelayTime = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var serviceScope = _serviceScopeFactory.CreateScope();

        var mcpProvider = serviceScope.ServiceProvider.GetRequiredService<McpProvider>();
        _ = await mcpProvider.GetMcpToolsAsync();

        _telegramBotService = serviceScope.ServiceProvider.GetRequiredService<TelegramBotService>();
        _telegramBotService.Start(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(DelayTime, stoppingToken);
        }
    }
}
