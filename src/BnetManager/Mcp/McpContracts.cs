using BnetManager.Core;

namespace BnetManager.Mcp;

public sealed record McpAccount(string Id, string Name, string BattleTag, BattleRegion Region,
    string RegionName, DateTimeOffset SavedAt, DateTimeOffset? LastLaunchedAt);

public sealed record McpStatus(string Version, bool Busy, bool DialogOpen, bool NeedsRecovery,
    bool ClientRunning, string? LastLaunchedAccountId);

public sealed record McpActionResult(bool Success, string Message);

public interface IMcpAccountGateway
{
    Task<McpStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<McpAccount[]> ListAccountsAsync(CancellationToken cancellationToken);
    Task<McpActionResult> SwitchAccountAsync(string accountId, CancellationToken cancellationToken);
    Task<McpActionResult> LoginNewAccountAsync(BattleRegion region, CancellationToken cancellationToken);
    Task<McpActionResult> SaveCurrentAccountAsync(string name, string battleTag, BattleRegion region, CancellationToken cancellationToken);
    Task<McpActionResult> RefreshAccountSessionAsync(string accountId, CancellationToken cancellationToken);
    Task<McpActionResult> EditAccountAsync(string accountId, string name, string? battleTag, CancellationToken cancellationToken);
    Task<McpActionResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken);
}
