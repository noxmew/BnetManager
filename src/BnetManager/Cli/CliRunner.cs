using System.Text.Json;
using BnetManager.Core;

namespace BnetManager.Cli;

internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliResponse response;
        try
        {
            var command = CliParser.Parse(args);
            if (command is CliHelpCommand) response = CliResponse.Ok(CliJson.Help);
            else if (command is CliVersionCommand) response = CliResponse.Ok(new CliVersionData(UpdateChecker.CurrentVersionText));
            else
            {
                using var lease = InstanceLease.TryAcquire();
                response = lease is null
                    ? await CliPipeClient.SendAsync(args)
                    : await CliExecutor.ExecuteAsync(command, new HeadlessAccountGateway(), CancellationToken.None);
            }
        }
        catch (Exception error) { response = CliExecutor.Failure(error); }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, CliJson.Options));
        return CliExecutor.ExitCode(response);
    }
}
