using System.Text.Json.Serialization;
using Pacos.Enums;

namespace Pacos.Models;

public sealed class McpServer
{
    [JsonPropertyName("type")]
    public ServerType Type { get; set; }

    [JsonIgnore]
    public string? Name { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public string[] Args { get; set; } = [];

    [JsonPropertyName("env")]
    public Dictionary<string, string?> Env { get; set; } = new();

    [JsonPropertyName("envFile")]
    public string? EnvFile { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();
}
