using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BnetManager.Core;

namespace BnetManager.Mcp;

public sealed record McpConfiguration
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 17861;
    public required string AccessToken { get; init; }
    [JsonIgnore]
    public string Endpoint => $"http://127.0.0.1:{Port}/mcp";

    public void Validate()
    {
        if (Port is < 1024 or > 65535) throw new InvalidDataException("MCP 端口需要在 1024～65535 之间。");
        if (AccessToken == null || AccessToken.Length != 64 || AccessToken.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("MCP 访问密钥无效。");
    }

    public static McpConfiguration Load(string dataRoot)
    {
        var path = Path.Combine(dataRoot, "mcp.json");
        if (!File.Exists(path)) return new() { AccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
        var configuration = JsonSerializer.Deserialize<McpConfiguration>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("MCP 设置为空。");
        configuration.Validate();
        return configuration;
    }

    public void Save(string dataRoot)
    {
        Validate();
        AccountStore.WriteAtomic(Path.Combine(dataRoot, "mcp.json"), JsonSerializer.SerializeToUtf8Bytes(this, JsonFormat.Options));
    }

    public string ConnectionJson() => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["bnetmanager"] = new { type = "http", url = Endpoint, headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {AccessToken}" } }
        }
    }, JsonFormat.Options);
}
