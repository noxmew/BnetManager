using System.Text.Json.Nodes;

namespace BnetManager.Core;

public sealed record LoginInfo(string Identity, BattleRegion Region);

public static class BattleConfiguration
{
    public static string ConfigPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", "Battle.net.config");
    public static string LocalPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Battle.net");

    public static string ClientDirectory(string launcher) => Path.GetFullPath(Path.GetDirectoryName(launcher)!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static JsonObject ClientScope(JsonObject root, string directory)
    {
        var scopes = root.Select(p => p.Value).OfType<JsonObject>()
            .Where(scope => scope["Path"] is JsonValue path && path.TryGetValue<string>(out var location)
                && Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(directory, StringComparison.OrdinalIgnoreCase)).ToList();
        if (scopes.Count != 1)
            throw new InvalidDataException("没有找到所选战网安装目录对应的唯一配置分区。请核对客户端设置，并先从这个目录启动一次官方战网。");
        return scopes[0];
    }

    public static LoginInfo ReadLogin(byte[] data, string launcher)
    {
        var root = JsonNode.Parse(data)?.AsObject() ?? throw new InvalidDataException("战网配置为空。");
        var identity = root["Client"]?["SavedAccountNames"]?.GetValue<string>()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(identity))
            throw new InvalidOperationException("尚未发现已保存的战网账号。请先在战网完成登录，并勾选保持登录。");
        var scope = ClientScope(root, ClientDirectory(launcher));
        var regionCode = scope["Services"]?["LastLoginRegion"]?.GetValue<string>()
            ?? throw new InvalidDataException("战网配置缺少登录区服，请先在官方客户端完成一次登录。");
        return new(identity, Regions.FromCode(regionCode));
    }

    public static byte[] NewLogin(byte[] currentConfig, string launcher, BattleRegion region)
    {
        var info = Regions.Get(region);
        var root = JsonNode.Parse(currentConfig)?.AsObject() ?? throw new InvalidDataException("战网配置为空。");
        var scope = ClientScope(root, ClientDirectory(launcher));
        foreach (var section in new[] { root, scope })
        {
            if (section["Client"] is not JsonObject client) throw new InvalidDataException("战网客户端配置分区缺少 Client 对象。");
            foreach (var key in new[] { "SavedAccountNames", "AutoLogin", "AutoLoginCN", "RememberAccountName" }) client.Remove(key);
        }
        var scopedClient = scope["Client"]!.AsObject();
        if (scopedClient["LoginSettings"] == null) scopedClient["LoginSettings"] = new JsonObject();
        var settings = scopedClient["LoginSettings"]!.AsObject();
        settings["AllowedRegions"] = info.AllowedRegions;
        settings["AllowedLocales"] = info.Locale;
        var services = scope["Services"]?.AsObject() ?? throw new InvalidDataException("战网客户端配置分区缺少 Services 对象。");
        services["LastLoginAddress"] = info.Address;
        services["LastLoginRegion"] = info.Code;
        services["LastLoginTassadar"] = info.AccountHost;
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root, JsonFormat.Options);
    }
}
