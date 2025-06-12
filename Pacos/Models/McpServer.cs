using System.Text.Json.Serialization;
using Pacos.Enums;

namespace Pacos.Models;

public sealed class McpServer
{
    [JsonPropertyName("type")]
    public ServerType Type { get; init; }

    [JsonIgnore]
    public string? Name { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public string[] Args { get; init; } = [];

    [JsonPropertyName("env")]
    public Dictionary<string, string?> Env { get; init; } = new();

    [JsonPropertyName("envFile")]
    public string? EnvFile { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; init; } = new();
}
