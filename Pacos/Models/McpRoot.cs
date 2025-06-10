using System.Text.Json.Serialization;

namespace Pacos.Models;

public class McpRoot
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServer>? McpServers { get; set; }
}
