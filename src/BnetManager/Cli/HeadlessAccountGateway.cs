using BnetManager.Core;
using BnetManager.Mcp;

namespace BnetManager.Cli;

public sealed class HeadlessAccountGateway : IMcpAccountGateway
{
    private readonly AccountManager manager = new();
    private static readonly IProgress<string> Progress = new SilentProgress();

    public Task<McpStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new McpStatus(UpdateChecker.CurrentVersionText, false, false,
            manager.NeedsRecovery, manager.Client.IsRunning, manager.Catalog.LastLaunchedId));
    }

    public Task<McpAccount[]> ListAccountsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(manager.Catalog.Accounts
            .OrderBy(account => account.Region)
            .ThenBy(account => account.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(account => new McpAccount(account.Id, account.Name, account.BattleTag,
                account.Region, account.RegionName, account.SavedAt, account.LastLaunchedAt))
            .ToArray());
    }

    public async Task<McpActionResult> SwitchAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        var account = FindAccount(accountId);
        await manager.SwitchAsync(account.Id, Progress).ConfigureAwait(false);
        return new McpActionResult(true, $"已启动「{account.Name}」 · {account.RegionName}。");
    }

    public async Task<McpActionResult> LoginNewAccountAsync(BattleRegion region, CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        ValidateRegion(region);
        await manager.LoginNewAsync(region, Progress).ConfigureAwait(false);
        return new McpActionResult(true, $"已打开{Regions.Get(region).Name}登录窗口。登录后保存账号。");
    }

    public async Task<McpActionResult> SaveCurrentAccountAsync(string name, string battleTag, BattleRegion region,
        CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        ValidateProfile(name, battleTag);
        ValidateRegion(region);
        await manager.SaveCurrentAsync(name, battleTag, "", region, null, Progress).ConfigureAwait(false);
        return new McpActionResult(true, "账号已保存。");
    }

    public async Task<McpActionResult> RefreshAccountSessionAsync(string accountId, CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        var account = FindAccount(accountId);
        ValidateProfile(account.Name, account.BattleTag);
        ValidateRegion(account.Region);
        await manager.SaveCurrentAsync(account.Name, account.BattleTag, account.Group,
            account.Region, account.Id, Progress).ConfigureAwait(false);
        return new McpActionResult(true, "登录状态已更新。");
    }

    public Task<McpActionResult> EditAccountAsync(string accountId, string name, string? battleTag,
        CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        var account = FindAccount(accountId);
        var effectiveBattleTag = battleTag ?? account.BattleTag;
        ValidateProfile(name, effectiveBattleTag);
        manager.UpdateProfile(account.Id, name, effectiveBattleTag, account.Group);
        return Task.FromResult(new McpActionResult(true, "账号信息已保存。"));
    }

    public Task<McpActionResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        EnsureReady(cancellationToken);
        var account = FindAccount(accountId);
        manager.Delete(account.Id);
        return Task.FromResult(new McpActionResult(true, "账号记录已移除。"));
    }

    private void EnsureReady(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (manager.NeedsRecovery)
            throw new InvalidOperationException("上次操作没有完成。请关闭游戏和战网后重新打开 BnetManager。");
    }

    private AccountProfile FindAccount(string accountId)
    {
        if (accountId is not { Length: 32 } || !Guid.TryParseExact(accountId, "N", out var id))
            throw new InvalidOperationException("账号标识无效，请重新获取账号列表。");
        var normalizedId = id.ToString("N");
        return manager.Catalog.Accounts.SingleOrDefault(account => account.Id == normalizedId)
            ?? throw new InvalidOperationException("账号记录不存在，请重新获取账号列表。");
    }

    private static void ValidateRegion(BattleRegion region)
    {
        if (!Enum.IsDefined(region)) throw new InvalidOperationException("请选择有效的战网区服。");
    }

    private static void ValidateProfile(string name, string battleTag)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40)
            throw new InvalidOperationException("账号备注需要 1～40 个字符。");
        if (battleTag is null || battleTag.Length > 80)
            throw new InvalidOperationException("战网昵称不能超过 80 个字符。");
    }

    private sealed class SilentProgress : IProgress<string>
    {
        public void Report(string value) { }
    }
}
