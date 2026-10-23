using System.Text.Json;
using System.Text.Json.Serialization;

namespace BnetManager.Core;

[JsonConverter(typeof(JsonStringEnumConverter<BattleRegion>))]
public enum BattleRegion { China, Asia, Americas, Europe }

public sealed record RegionInfo(BattleRegion Region, string Name, string Code, string Address, string AccountHost, string AllowedRegions, string Locale)
{
    public override string ToString() => Name;
}

public static class Regions
{
    public static IReadOnlyList<RegionInfo> All { get; } =
    [
        new(BattleRegion.China, "国服", "CN", "cn.actual.battlenet.com.cn", "account.battlenet.com.cn", "CN", "zhCN"),
        new(BattleRegion.Asia, "亚服", "KR", "kr.actual.battle.net", "account.battle.net", "", ""),
        new(BattleRegion.Americas, "美服", "US", "us.actual.battle.net", "account.battle.net", "US", ""),
        new(BattleRegion.Europe, "欧服", "EU", "eu.actual.battle.net", "account.battle.net", "EU", "")
    ];

    public static RegionInfo Get(BattleRegion region) => All.Single(r => r.Region == region);
    public static BattleRegion FromCode(string code) => All.SingleOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Region
        ?? throw new InvalidDataException("无法识别战网当前区服，请先在官方客户端完成一次登录。");
}

public sealed record AccountProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string LoginIdentity { get; init; }
    public string BattleTag { get; init; } = "";
    public string Group { get; init; } = "常用账号";
    public required BattleRegion Region { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? LastLaunchedAt { get; init; }
    [JsonIgnore] public string RegionName => Regions.Get(Region).Name;
    [JsonIgnore] public string RegionCode => Regions.Get(Region).Code;
    [JsonIgnore] public string Initial => string.IsNullOrWhiteSpace(Name) ? "B" : Name[..1].ToUpperInvariant();
    [JsonIgnore] public string LastUsedText => LastLaunchedAt is { } date ? $"上次启动 {date.ToLocalTime():MM/dd HH:mm}" : "未启动";
    [JsonIgnore] public string SnapshotText => $"状态更新 {SavedAt.ToLocalTime():yyyy/MM/dd HH:mm}";
    public override string ToString() => $"{Name} · {RegionName}";
}

public sealed record AccountCatalog
{
    public int Version { get; init; } = 1;
    public string? LauncherPath { get; set; }
    public string? LastLaunchedId { get; set; }
    public List<AccountProfile> Accounts { get; init; } = [];
}

public static class JsonFormat
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true };
}
