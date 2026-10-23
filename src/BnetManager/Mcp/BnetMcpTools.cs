using System.ComponentModel;
using BnetManager.Core;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace BnetManager.Mcp;

public sealed class BnetMcpTools(IMcpAccountGateway gateway)
{
    [McpServerTool(Name = "get_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get application, Battle.net client, and account operation status.")]
    public Task<McpStatus> GetStatusAsync(CancellationToken cancellationToken)
        => InvokeAsync(() => gateway.GetStatusAsync(cancellationToken));

    [McpServerTool(Name = "list_accounts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List saved account IDs, names, regions, and timestamps. Login snapshots are not returned.")]
    public Task<McpAccount[]> ListAccountsAsync(CancellationToken cancellationToken)
        => InvokeAsync(() => gateway.ListAccountsAsync(cancellationToken));

    [McpServerTool(Name = "switch_account", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Switch to a saved account and launch Battle.net. Exit running games first.")]
    public Task<McpActionResult> SwitchAccountAsync(
        [Description("Saved account ID from list_accounts.")] string accountId,
        CancellationToken cancellationToken)
        => InvokeActionAsync(() => gateway.SwitchAccountAsync(accountId, cancellationToken));

    [McpServerTool(Name = "login_new_account", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Open official Battle.net for a new login in the selected region. Exit running games first.")]
    public Task<McpActionResult> LoginNewAccountAsync(
        [Description("Battle.net region: China, Asia, Americas, or Europe.")] BattleRegion region,
        CancellationToken cancellationToken)
        => InvokeActionAsync(() => gateway.LoginNewAccountAsync(region, cancellationToken));

    [McpServerTool(Name = "save_current_account", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Save the current Battle.net login as an account. Exit running games first.")]
    public Task<McpActionResult> SaveCurrentAccountAsync(
        [Description("Account display name, 1 to 40 characters.")] string name,
        [Description("Current Battle.net region: China, Asia, Americas, or Europe.")] BattleRegion region,
        CancellationToken cancellationToken,
        [Description("Optional BattleTag, up to 80 characters.")] string battleTag = "")
        => InvokeActionAsync(() => gateway.SaveCurrentAccountAsync(name, battleTag, region, cancellationToken));

    [McpServerTool(Name = "refresh_account_session", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Replace an account's saved session with its current Battle.net login. Exit running games first.")]
    public Task<McpActionResult> RefreshAccountSessionAsync(
        [Description("Saved account ID from list_accounts.")] string accountId,
        CancellationToken cancellationToken)
        => InvokeActionAsync(() => gateway.RefreshAccountSessionAsync(accountId, cancellationToken));

    [McpServerTool(Name = "edit_account", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Update an account's display name and BattleTag without changing its login session.")]
    public Task<McpActionResult> EditAccountAsync(
        [Description("Saved account ID from list_accounts.")] string accountId,
        [Description("Account display name, 1 to 40 characters.")] string name,
        CancellationToken cancellationToken,
        [Description("BattleTag, up to 80 characters. Empty clears the saved BattleTag.")] string battleTag = "")
        => InvokeActionAsync(() => gateway.EditAccountAsync(accountId, name, battleTag, cancellationToken));

    [McpServerTool(Name = "remove_account", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Delete a local saved account and its login snapshot. This does not delete the online Battle.net account.")]
    public Task<McpActionResult> RemoveAccountAsync(
        [Description("Saved account ID from list_accounts.")] string accountId,
        CancellationToken cancellationToken)
        => InvokeActionAsync(() => gateway.RemoveAccountAsync(accountId, cancellationToken));

    private static async Task<McpActionResult> InvokeActionAsync(Func<Task<McpActionResult>> operation)
    {
        var result = await InvokeAsync(operation).ConfigureAwait(false);
        if (!result.Success) throw new McpException(result.Message);
        return result;
    }

    private static async Task<T> InvokeAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            var message = error.Message.Split(['\r', '\n'], 2)[0];
            throw new McpException(message);
        }
    }
}
