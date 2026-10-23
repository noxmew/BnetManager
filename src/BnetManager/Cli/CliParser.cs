using BnetManager.Core;

namespace BnetManager.Cli;

public static class CliParser
{
    public static CliCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 1 && args[0] == "--help") return new CliHelpCommand();
        if (args.Length == 1 && args[0] == "--version") return new CliVersionCommand();
        if (args.Length == 0 || args[0] != "cli")
            throw Usage("CLI 命令需要以 cli 开头。");
        if (args.Length == 1) throw Usage("缺少命令，请使用 cli --help 查看用法。");

        return args[1] switch
        {
            "--help" => WithoutArguments(args, 2, new CliHelpCommand()),
            "--version" => WithoutArguments(args, 2, new CliVersionCommand()),
            "status" => WithoutArguments(args, 2, new CliStatusCommand()),
            "accounts" => ParseAccounts(args),
            "login" => ParseLogin(args),
            _ => throw Usage($"未知命令：{args[1]}。")
        };
    }

    private static CliCommand ParseAccounts(string[] args)
    {
        if (args.Length < 3) throw Usage("缺少 accounts 子命令。");
        return args[2] switch
        {
            "list" => WithoutArguments(args, 3, new CliListAccountsCommand()),
            "switch" => new CliSwitchAccountCommand(SingleAccountId(args)),
            "save" => ParseSave(args),
            "refresh" => new CliRefreshAccountCommand(SingleAccountId(args)),
            "edit" => ParseEdit(args),
            "remove" => new CliRemoveAccountCommand(SingleAccountId(args)),
            _ => throw Usage($"未知 accounts 子命令：{args[2]}。")
        };
    }

    private static CliSaveAccountCommand ParseSave(string[] args)
    {
        var options = ParseOptions(args, 3, "--name", "--region", "--battle-tag");
        return new CliSaveAccountCommand(Name(Required(options, "--name")),
            BattleTag(options.GetValueOrDefault("--battle-tag", "")), Region(Required(options, "--region")));
    }

    private static CliEditAccountCommand ParseEdit(string[] args)
    {
        if (args.Length < 4) throw Usage("缺少账号标识。");
        var accountId = AccountId(args[3]);
        var options = ParseOptions(args, 4, "--name", "--battle-tag");
        return new CliEditAccountCommand(accountId, Name(Required(options, "--name")),
            options.TryGetValue("--battle-tag", out var battleTag) ? BattleTag(battleTag) : null);
    }

    private static CliLoginCommand ParseLogin(string[] args)
    {
        var options = ParseOptions(args, 2, "--region");
        return new CliLoginCommand(Region(Required(options, "--region")));
    }

    private static T WithoutArguments<T>(string[] args, int expectedLength, T command) where T : CliCommand
    {
        if (args.Length != expectedLength) throw Usage("命令包含多余参数。");
        return command;
    }

    private static string SingleAccountId(string[] args)
    {
        if (args.Length < 4) throw Usage("缺少账号标识。");
        if (args.Length != 4) throw Usage("命令包含多余参数。");
        return AccountId(args[3]);
    }

    private static Dictionary<string, string> ParseOptions(string[] args, int offset, params string[] allowed)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = offset; index < args.Length; index += 2)
        {
            var option = args[index];
            if (!allowed.Contains(option, StringComparer.Ordinal)) throw Usage($"未知参数：{option}。");
            if (options.ContainsKey(option)) throw Usage($"参数重复：{option}。");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw Usage($"参数 {option} 缺少值。");
            options.Add(option, args[index + 1]);
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw Usage($"缺少必需参数：{name}。");

    private static string AccountId(string value)
    {
        if (value.Length != 32 || !Guid.TryParseExact(value, "N", out var id))
            throw Usage("账号标识必须为账号列表返回的 32 位十六进制 ID。");
        return id.ToString("N");
    }

    private static string Name(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > 40) throw Usage("账号备注需要 1～40 个字符。");
        return name;
    }

    private static string BattleTag(string value)
    {
        if (value.Length > 80) throw Usage("战网昵称不能超过 80 个字符。");
        return value.Trim();
    }

    private static BattleRegion Region(string value) => value.ToLowerInvariant() switch
    {
        "china" => BattleRegion.China,
        "asia" => BattleRegion.Asia,
        "americas" => BattleRegion.Americas,
        "europe" => BattleRegion.Europe,
        _ => throw Usage("区服必须为 China、Asia、Americas 或 Europe。")
    };

    private static CliUsageException Usage(string message) => new(message);
}
