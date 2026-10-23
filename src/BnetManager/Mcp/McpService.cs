namespace BnetManager.Mcp;

public sealed class McpService
{
    private readonly string dataRoot;
    private readonly McpServerHost host;
    private readonly SemaphoreSlim configurationGate = new(1, 1);
    public McpConfiguration Configuration { get; private set; }
    public bool IsRunning => host.IsRunning;
    public bool IsChanging { get; private set; }
    public string? LastError { get; private set; }
    public event Action? Changed;

    public McpService(IMcpAccountGateway gateway, string dataRoot)
    {
        this.dataRoot = dataRoot;
        Configuration = McpConfiguration.Load(dataRoot);
        host = new(gateway);
    }

    public async Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken)
    {
        var configuration = Configuration with { Enabled = enabled, Port = port };
        configuration.Validate();
        if (!await configurationGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("MCP 服务正在更新设置，请稍候。");
        IsChanging = true;
        LastError = null;
        try
        {
            Changed?.Invoke();
            if (IsRunning && enabled && port != Configuration.Port)
                throw new InvalidOperationException("请先关闭 MCP 服务，再修改端口。");
            await Task.Run(() => configuration.Save(dataRoot), cancellationToken);
            Configuration = configuration;
            if (enabled) await host.StartAsync(configuration, cancellationToken);
            else await host.StopAsync(cancellationToken);
        }
        catch (Exception error)
        {
            LastError = error is OperationCanceledException ? "MCP 操作已取消。" : error.Message;
            throw;
        }
        finally
        {
            IsChanging = false;
            configurationGate.Release();
            Changed?.Invoke();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await configurationGate.WaitAsync(cancellationToken);
        try { await host.StopAsync(cancellationToken); }
        finally { configurationGate.Release(); }
    }
}
