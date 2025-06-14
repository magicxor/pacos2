using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Pacos.Enums;
using Pacos.Models;

namespace Pacos.Services.Mcp;

public sealed class McpProvider
{
    private static readonly JsonSerializerOptions McpRootJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ILogger<McpProvider> _logger;
    private readonly string _mcpConfigJson;

    private volatile IReadOnlyCollection<IMcpClient>? _currentMcpClients;
    private volatile IList<AITool>? _currentMcpTools;

    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    public McpProvider(
        ILogger<McpProvider> logger,
        string mcpConfigJson)
    {
        _logger = logger;
        _mcpConfigJson = mcpConfigJson;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "McpClientFactory handles disposal of the client transport")]
    private async Task<IMcpClient?> BuildMcpClientFromModelAsync(string mcpServerName, McpServer mcpServerModel)
    {
        mcpServerModel.Name = mcpServerName;
        _logger.LogInformation("Parsing server '{ServerName}' of type {ServerType}", mcpServerName, mcpServerModel.Type);

        var type = mcpServerModel.Type;
        if (type is ServerType.Unspecified)
        {
            type = mcpServerModel.Url is { Length: > 0 } ? ServerType.Sse : ServerType.Stdio;
            _logger.LogInformation("Server '{ServerName}' recognized as {ServerType}", mcpServerName, type);
        }

        try
        {
            switch (type)
            {
                case ServerType.Sse:
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerModel.Url);
                    var clientTransport = new SseClientTransport(
                        new SseClientTransportOptions
                        {
                            Name = mcpServerName,
                            Endpoint = new Uri(mcpServerModel.Url),
                        });
                    return await McpClientFactory.CreateAsync(clientTransport);
                }
                case ServerType.Stdio:
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(mcpServerModel.Command);
                    var clientTransport = new StdioClientTransport(
                        new StdioClientTransportOptions
                        {
                            Name = mcpServerName,
                            Command = mcpServerModel.Command,
                            Arguments = mcpServerModel.Args,
                            EnvironmentVariables = mcpServerModel.Env,
                        });
                    return await McpClientFactory.CreateAsync(clientTransport);
                }
                default:
                    throw new NotSupportedException($"Unsupported MCP server type: {type}");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create MCP client for server '{ServerName}' of type {ServerType}", mcpServerName, type);
        }

        return null;
    }

    private async Task<IReadOnlyCollection<IMcpClient>> BuildMcpClientsFromConfigAsync()
    {
        var mcpRoot = JsonSerializer.Deserialize<McpRoot>(_mcpConfigJson, McpRootJsonSerializerOptions);

        List<IMcpClient> clients = [];
        if (mcpRoot?.McpServers == null || mcpRoot.McpServers.Count == 0)
        {
            _logger.LogError("No MCP servers found in the configuration");
            return clients;
        }

        foreach (var (mcpServerName, mcpServerModel) in mcpRoot.McpServers)
        {
            var client = await BuildMcpClientFromModelAsync(mcpServerName, mcpServerModel);
            if (client != null)
            {
                clients.Add(client);
            }
            else
            {
                _logger.LogError("Failed to create MCP client for server '{ServerName}' of type {ServerType}", mcpServerName, mcpServerModel.Type);
            }
        }

        return clients.AsReadOnly();
    }

    public async Task<IList<AITool>> GetMcpToolsAsync()
    {
        await GetMcpClientsAsync();
        return _currentMcpTools ?? [];
    }

    public async Task<IReadOnlyCollection<IMcpClient>> GetMcpClientsAsync()
    {
        if (_currentMcpClients != null)
            return _currentMcpClients;

        await Semaphore.WaitAsync();

        try
        {
            _currentMcpClients = await BuildMcpClientsFromConfigAsync();
            var mcpTools = new List<AITool>();

            foreach (var mcpClient in _currentMcpClients)
            {
                mcpTools.AddRange(await mcpClient.ListToolsAsync());
            }

            _currentMcpTools = mcpTools;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while getting MCP clients");
        }
        finally
        {
            Semaphore.Release();
        }

        return _currentMcpClients ?? [];
    }
}
