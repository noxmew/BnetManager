using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BnetManager.Core;
using BnetManager.Mcp;

namespace BnetManager.Cli;

internal sealed record CliPipeRequest(int Protocol, string[] Arguments);

internal static class CliPipeProtocol
{
    public const int RequestLimit = 64 * 1024;
    public const int ResponseLimit = 4 * 1024 * 1024;
    public static string Name { get; } = "BnetManager.Cli." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(InstanceLease.DataRoot.ToUpperInvariant())))[..24];

    public static async Task WriteAsync<T>(Stream stream, T value, int limit, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, CliJson.Options);
        if (bytes.Length > limit) throw new InvalidDataException("CLI 消息超过大小限制。");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 || length > limit) throw new InvalidDataException("CLI 消息长度无效。");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, CliJson.Options)
            ?? throw new InvalidDataException("CLI 消息为空。");
    }
}

internal static class CliPipeClient
{
    public static async Task<CliResponse> SendAsync(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", CliPipeProtocol.Name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using (var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
            using (var writing = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await CliPipeProtocol.WriteAsync(pipe, new CliPipeRequest(1, args), CliPipeProtocol.RequestLimit, writing.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return CliResponse.Fail("unavailable", "已有实例正在执行命令、初始化或退出，暂时无法连接。若运行的是旧版本，请关闭旧程序后重试。");
        }

        try
        {
            using var responseTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var response = await CliPipeProtocol.ReadAsync<CliResponse>(pipe, CliPipeProtocol.ResponseLimit, responseTimeout.Token).ConfigureAwait(false);
            if (!response.Success && response.Error is null) throw new InvalidDataException("CLI 响应缺少错误信息。");
            return response;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
        {
            return CliResponse.Fail("unavailable", "未收到命令结果；操作可能仍在进行。请先查询状态，不要立即重复执行。");
        }
    }
}

internal sealed class CliPipeServer(IMcpAccountGateway gateway)
{
    private readonly CancellationTokenSource lifetime = new();
    private Task[] workers = [];

    public void Start()
    {
        var listeners = new List<NamedPipeServerStream>();
        try
        {
            for (var index = 0; index < 4; index++) listeners.Add(CreatePipe());
            workers = listeners.Select(ServeAsync).ToArray();
        }
        catch
        {
            foreach (var listener in listeners) listener.Dispose();
            throw;
        }
    }

    private static NamedPipeServerStream CreatePipe() => new(CliPipeProtocol.Name, PipeDirection.InOut, 4,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ServeAsync(NamedPipeServerStream initial)
    {
        var pipe = initial;
        while (true)
        {
            using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                    CliResponse response;
                    try
                    {
                        using var reading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        reading.CancelAfter(TimeSpan.FromSeconds(5));
                        var request = await CliPipeProtocol.ReadAsync<CliPipeRequest>(pipe, CliPipeProtocol.RequestLimit, reading.Token).ConfigureAwait(false);
                        if (request.Protocol != 1 || request.Arguments is null || request.Arguments.Length > 32
                            || request.Arguments.Any(argument => argument is null))
                            throw new CliUsageException("CLI 请求格式或协议版本无效。");
                        var command = CliParser.Parse(request.Arguments);
                        response = await CliExecutor.ExecuteAsync(command, gateway, lifetime.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is CliUsageException or JsonException or InvalidDataException)
                    {
                        response = CliResponse.Fail("usage", error.Message);
                    }
                    using var writing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    writing.CancelAfter(TimeSpan.FromSeconds(5));
                    await CliPipeProtocol.WriteAsync(pipe, response, CliPipeProtocol.ResponseLimit, writing.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
            if (lifetime.IsCancellationRequested) return;
            pipe = CreatePipe();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
