using System.Net.Http;
using System.Text.Json;

namespace BnetManager.Core;

public sealed record UpdateRelease(Version Version, Uri PageUri);

public static class UpdateChecker
{
    private const string RepositoryPage = "https://github.com/noxmew/BnetManager";
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(6);

    public static Version CurrentVersion { get; } = ReadCurrentVersion();
    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = new CancellationTokenSource();
        var token = deadline.Token;
        try
        {
            return await Task.Run(() => ReadReleaseAsync(token), CancellationToken.None)
                .WaitAsync(CheckTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("检查更新超时，请稍后重试。");
        }
        finally
        {
            _ = CancelAndDisposeAsync(deadline);
        }
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        finally { cancellation.Dispose(); }
    }

    private static Version ReadCurrentVersion()
    {
        var version = typeof(UpdateChecker).Assembly.GetName().Version
            ?? throw new InvalidOperationException("程序版本信息缺失。");
        return new Version(version.Major, version.Minor, version.Build);
    }

    private static async Task<UpdateRelease?> ReadReleaseAsync(CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/noxmew/BnetManager/releases/latest");
        request.Headers.UserAgent.ParseAdd($"BnetManager/{CurrentVersionText}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidDataException("更新信息超过大小限制。");

            using var body = new MemoryStream();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > MaximumResponseBytes)
                    throw new InvalidDataException("更新信息超过大小限制。");
                body.Write(buffer, 0, count);
            }

            using var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("更新信息不是正式版本。");

            var tag = ReadString(root, "tag_name");
            if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version)
                || version.Build < 0 || version.Revision >= 0 || tag != $"v{version.ToString(3)}")
                throw new InvalidDataException("更新版本号无效。");

            var page = $"{RepositoryPage}/releases/tag/{tag}";
            if (ReadString(root, "html_url") != page)
                throw new InvalidDataException("更新页面地址无效。");

            var expectedAsset = $"BnetManager-{version.ToString(3)}-win-x64.zip";
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array
                || !assets.EnumerateArray().Any(asset => asset.ValueKind == JsonValueKind.Object
                    && asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && name.GetString() == expectedAsset
                    && asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
                    && state.GetString() == "uploaded"))
                throw new InvalidDataException("正式版本缺少可下载的 Windows x64 安装包。");

            return version > CurrentVersion ? new UpdateRelease(version, new Uri(page)) : null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("更新信息格式无效。", exception);
        }
        catch (IOException exception)
        {
            throw new HttpRequestException("无法读取更新信息。", exception);
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("更新信息缺少必要内容。");
        return value.GetString()!;
    }
}
