using System.Windows.Threading;
using BnetManager.Core;
using BnetManager.Mcp;

namespace BnetManager;

public partial class MainWindow : IMcpAccountGateway
{
    private bool shuttingDown;
    private McpAccount[] mcpAccounts = [];
    private string? mcpLastLaunchedAccountId;

    private void RefreshMcpAccounts()
    {
        mcpAccounts = manager.Catalog.Accounts
            .OrderBy(account => account.Region)
            .ThenBy(account => account.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(account => new McpAccount(account.Id, account.Name, account.BattleTag,
                account.Region, account.RegionName, account.SavedAt, account.LastLaunchedAt))
            .ToArray();
        mcpLastLaunchedAccountId = manager.Catalog.LastLaunchedId;
    }

    private Task<T> InvokeMcpAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shuttingDown || Dispatcher.HasShutdownStarted)
                throw new OperationBusyException("BnetManager 正在退出，请稍后重新连接。");
            return await action();
        }, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }

    private void EnsureMcpMutationReady()
    {
        if (busy) throw new OperationBusyException("另一个账号操作正在进行，请稍候。");
        if (OwnedWindows.Count > 0) throw new OperationBusyException("BnetManager 有窗口正在等待操作，请先关闭该窗口。");
        if (manager.NeedsRecovery)
            throw new InvalidOperationException("上次操作没有完成。请关闭游戏和战网后重新打开 BnetManager。");
    }

    private AccountProfile FindMcpAccount(string accountId)
    {
        if (!Guid.TryParseExact(accountId, "N", out _))
            throw new InvalidOperationException("账号标识无效，请重新获取账号列表。");
        return manager.Catalog.Accounts.SingleOrDefault(account => account.Id == accountId)
            ?? throw new InvalidOperationException("账号记录不存在，请重新获取账号列表。");
    }

    private static void ValidateMcpRegion(BattleRegion region)
    {
        if (!Enum.IsDefined(region)) throw new InvalidOperationException("请选择有效的战网区服。");
    }

    private static void ValidateMcpProfile(string name, string battleTag)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40)
            throw new InvalidOperationException("账号备注需要 1～40 个字符。");
        if (battleTag == null || battleTag.Length > 80)
            throw new InvalidOperationException("战网昵称不能超过 80 个字符。");
    }

    public Task<McpStatus> GetStatusAsync(CancellationToken cancellationToken) => InvokeMcpAsync(() =>
        Task.FromResult(new McpStatus(UpdateChecker.CurrentVersionText, busy, OwnedWindows.Count > 0,
            manager.NeedsRecovery, IsClientRunning, mcpLastLaunchedAccountId)), cancellationToken);

    public Task<McpAccount[]> ListAccountsAsync(CancellationToken cancellationToken) => InvokeMcpAsync(() =>
        Task.FromResult(mcpAccounts.ToArray()), cancellationToken);

    public Task<McpActionResult> SwitchAccountAsync(string accountId, CancellationToken cancellationToken) =>
        InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            var account = FindMcpAccount(accountId);
            var message = $"已启动「{account.Name}」 · {account.RegionName}。";
            await ExecuteOperationAsync(progress => manager.SwitchAsync(account.Id, progress), message);
            return new McpActionResult(true, message);
        }, cancellationToken);

    public Task<McpActionResult> LoginNewAccountAsync(BattleRegion region, CancellationToken cancellationToken) =>
        InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            ValidateMcpRegion(region);
            var message = $"已打开{Regions.Get(region).Name}登录窗口。登录后保存账号。";
            await ExecuteOperationAsync(progress => manager.LoginNewAsync(region, progress), message);
            return new McpActionResult(true, message);
        }, cancellationToken);

    public Task<McpActionResult> SaveCurrentAccountAsync(string name, string battleTag, BattleRegion region,
        CancellationToken cancellationToken) => InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            ValidateMcpProfile(name, battleTag);
            ValidateMcpRegion(region);
            const string message = "账号已保存。";
            await ExecuteOperationAsync(progress => manager.SaveCurrentAsync(name, battleTag, "", region, null, progress), message);
            return new McpActionResult(true, message);
        }, cancellationToken);

    public Task<McpActionResult> RefreshAccountSessionAsync(string accountId, CancellationToken cancellationToken) =>
        InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            var account = FindMcpAccount(accountId);
            const string message = "登录状态已更新。";
            await ExecuteOperationAsync(progress => manager.SaveCurrentAsync(account.Name, account.BattleTag,
                account.Group, account.Region, account.Id, progress), message);
            return new McpActionResult(true, message);
        }, cancellationToken);

    public Task<McpActionResult> EditAccountAsync(string accountId, string name, string? battleTag,
        CancellationToken cancellationToken) => InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            var account = FindMcpAccount(accountId);
            battleTag ??= account.BattleTag;
            ValidateMcpProfile(name, battleTag);
            const string message = "账号信息已保存。";
            await ExecuteOperationAsync(_ =>
            {
                manager.UpdateProfile(account.Id, name, battleTag, account.Group);
                return Task.CompletedTask;
            }, message);
            return new McpActionResult(true, message);
        }, cancellationToken);

    public Task<McpActionResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken) =>
        InvokeMcpAsync(async () =>
        {
            EnsureMcpMutationReady();
            var account = FindMcpAccount(accountId);
            const string message = "账号记录已移除。";
            await ExecuteOperationAsync(_ =>
            {
                manager.Delete(account.Id);
                return Task.CompletedTask;
            }, message);
            return new McpActionResult(true, message);
        }, cancellationToken);
}
