using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BnetManager.Core;

namespace BnetManager.Cli;

public abstract record CliCommand(string Verb);
public sealed record CliHelpCommand() : CliCommand("help");
public sealed record CliVersionCommand() : CliCommand("version");
public sealed record CliStatusCommand() : CliCommand("status");
public sealed record CliListAccountsCommand() : CliCommand("accounts.list");
public sealed record CliSwitchAccountCommand(string AccountId) : CliCommand("accounts.switch");
public sealed record CliSaveAccountCommand(string Name, string BattleTag, BattleRegion Region) : CliCommand("accounts.save");
public sealed record CliRefreshAccountCommand(string AccountId) : CliCommand("accounts.refresh");
public sealed record CliEditAccountCommand(string AccountId, string Name, string? BattleTag) : CliCommand("accounts.edit");
public sealed record CliRemoveAccountCommand(string AccountId) : CliCommand("accounts.remove");
public sealed record CliLoginCommand(BattleRegion Region) : CliCommand("login");

public sealed record CliError(string Code, string Message);
public sealed record CliResponse(bool Success, object? Data, CliError? Error)
{
    public static CliResponse Ok(object data) => new(true, data, null);
    public static CliResponse Fail(string code, string message) => new(false, null, new CliError(code, message));
}

public sealed record CliVersionData(string Version);
public sealed record CliCommandHelp(string Usage, string Description);
public sealed record CliHelpData(string Usage, CliCommandHelp[] Commands, string[] Regions,
    Dictionary<string, int> ExitCodes);

public static class CliJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static CliHelpData Help { get; } = new("BnetManager.exe cli <command>",
    [
        new("BnetManager.exe cli status", "读取程序和战网状态。"),
        new("BnetManager.exe cli accounts list", "列出已保存的账号。"),
        new("BnetManager.exe cli accounts switch <id>", "切换到指定账号并启动战网。"),
        new("BnetManager.exe cli accounts save --name <name> --region <region> [--battle-tag <tag>]", "保存当前战网账号。"),
        new("BnetManager.exe cli accounts refresh <id>", "用当前登录状态更新指定账号。"),
        new("BnetManager.exe cli accounts edit <id> --name <name> [--battle-tag <tag>]", "修改账号信息，未传 battle-tag 时保留原昵称。"),
        new("BnetManager.exe cli accounts remove <id>", "移除指定账号的本地记录。"),
        new("BnetManager.exe cli login --region <region>", "打开指定区服的新账号登录入口。"),
        new("BnetManager.exe cli --help", "输出命令帮助。"),
        new("BnetManager.exe cli --version", "输出程序版本。")
    ], ["China", "Asia", "Americas", "Europe"], new Dictionary<string, int>
    {
        ["success"] = 0,
        ["operationFailed"] = 1,
        ["usage"] = 2,
        ["busy"] = 3,
        ["unavailable"] = 4
    });
}

public sealed class CliUsageException(string message) : Exception(message) { }
