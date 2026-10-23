using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BnetManager.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace BnetManager.Mcp;

public sealed class McpServerHost(IMcpAccountGateway gateway)
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private WebApplication? application;
    private volatile bool isRunning;

    public bool IsRunning => isRunning;

    public async Task StartAsync(McpConfiguration configuration, CancellationToken cancellationToken)
    {
        configuration.Validate();
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (application is not null)
                throw new InvalidOperationException("MCP 服务已经启动。");

            application = await Task.Run(async () =>
            {
                var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
                {
                    Args = [],
                    ApplicationName = typeof(McpServerHost).Assembly.GetName().Name,
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = Environments.Production
                });
                builder.Configuration.Sources.Clear();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, configuration.Port);
                    options.Limits.MaxRequestBodySize = 64 * 1024;
                });
                builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));
                builder.Services.AddMcpServer(options =>
                {
                    options.ServerInfo = new()
                    {
                        Name = "BnetManager",
                        Version = UpdateChecker.CurrentVersionText
                    };
                })
                    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                    .WithTools(new BnetMcpTools(gateway), new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
                    });

                var app = builder.Build();
                var expectedHost = $"127.0.0.1:{configuration.Port}";
                var expectedToken = Encoding.ASCII.GetBytes(configuration.AccessToken);
                app.Use(async (context, next) =>
                {
                    if (!string.Equals(context.Request.Host.Value, expectedHost, StringComparison.Ordinal)
                        || context.Request.Headers.ContainsKey("Origin"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    var authorization = context.Request.Headers.Authorization;
                    const string bearerPrefix = "Bearer ";
                    if (authorization.Count != 1
                        || authorization[0] is not { } value
                        || !value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                        || value.Length != bearerPrefix.Length + expectedToken.Length
                        || !CryptographicOperations.FixedTimeEquals(
                            Encoding.ASCII.GetBytes(value[bearerPrefix.Length..]), expectedToken))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        return;
                    }

                    await next(context).ConfigureAwait(false);
                });
                app.MapMcp("/mcp");
                app.Lifetime.ApplicationStopped.Register(() => isRunning = false);
                try
                {
                    await app.StartAsync(cancellationToken).ConfigureAwait(false);
                    return app;
                }
                catch
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
            isRunning = true;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (application is not { } app) return;
            application = null;
            isRunning = false;
            await Task.Run(async () =>
            {
                try
                {
                    await app.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
    }
}
