using BnetManager.Core;
using BnetManager.Mcp;

namespace BnetManager.Cli;

internal static class CliExecutor
{
    public static async Task<CliResponse> ExecuteAsync(CliCommand command, IMcpAccountGateway gateway,
        CancellationToken cancellationToken)
    {
        try
        {
            object data = command switch
            {
                CliStatusCommand => await gateway.GetStatusAsync(cancellationToken),
                CliListAccountsCommand => await gateway.ListAccountsAsync(cancellationToken),
                CliSwitchAccountCommand item => await gateway.SwitchAccountAsync(item.AccountId, cancellationToken),
                CliSaveAccountCommand item => await gateway.SaveCurrentAccountAsync(item.Name, item.BattleTag, item.Region, cancellationToken),
                CliRefreshAccountCommand item => await gateway.RefreshAccountSessionAsync(item.AccountId, cancellationToken),
                CliEditAccountCommand item => await gateway.EditAccountAsync(item.AccountId, item.Name, item.BattleTag, cancellationToken),
                CliRemoveAccountCommand item => await gateway.RemoveAccountAsync(item.AccountId, cancellationToken),
                CliLoginCommand item => await gateway.LoginNewAccountAsync(item.Region, cancellationToken),
                _ => throw new CliUsageException("该命令不需要账号服务。")
            };
            return CliResponse.Ok(data);
        }
        catch (Exception error) { return Failure(error); }
    }

    public static CliResponse Failure(Exception error) => error switch
    {
        CliUsageException => CliResponse.Fail("usage", error.Message),
        OperationBusyException => CliResponse.Fail("busy", error.Message),
        OperationCanceledException => CliResponse.Fail("unavailable", "程序正在退出，请稍后重试。"),
        _ => CliResponse.Fail("operationFailed", error.Message)
    };

    public static int ExitCode(CliResponse response) => response.Success ? 0 : response.Error?.Code switch
    {
        "usage" => 2,
        "busy" => 3,
        "unavailable" => 4,
        _ => 1
    };
}
