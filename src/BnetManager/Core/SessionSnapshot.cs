using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BnetManager.Core;

public sealed record SessionManifest(int Version, DateTimeOffset CapturedAt, bool HasConfig, string? Identity, BattleRegion? Region, string ClientDirectory);

public sealed class SessionImage(SessionManifest manifest, RegistryImage registry, Dictionary<string, byte[]> files) : IDisposable
{
    public SessionManifest Manifest { get; } = manifest;
    public RegistryImage Registry { get; } = registry;
    public Dictionary<string, byte[]> Files { get; } = files;

    public void Dispose()
    {
        foreach (var bytes in Files.Values) CryptographicOperations.ZeroMemory(bytes);
        Files.Clear();
    }
}

public static class SessionSnapshot
{
    private static readonly string[] LocalDirectories = ["Account", "BrowserCaches", "Cache"];
    private static readonly string[] LocalFiles = ["CachedData.db", "CachedData.db-wal", "CachedData.db-shm"];
    private const long MaxBytes = 512L * 1024 * 1024;
    private const int MaxFiles = 30000;
    private static readonly byte[] Header = "BnetManager.Session.v1\0"u8.ToArray();

    public static SessionImage Capture(string launcher, bool requireLogin)
    {
        Dictionary<string, byte[]> files = [];
        try
        {
            ValidateLocalRoot();
            if (File.Exists(BattleConfiguration.ConfigPath)) files.Add("config.json", File.ReadAllBytes(BattleConfiguration.ConfigPath));
            LoginInfo? login = null;
            if (requireLogin)
            {
                if (!files.TryGetValue("config.json", out var config)) throw new InvalidOperationException("未发现战网配置，请先登录战网。");
                login = BattleConfiguration.ReadLogin(config, launcher);
            }
            long size = files.Values.Sum(v => (long)v.Length);
            foreach (var name in LocalDirectories)
            {
                var directory = Path.Combine(BattleConfiguration.LocalPath, name);
                if (Directory.Exists(directory)) CaptureDirectory(directory, "local/" + name, files, ref size);
            }
            foreach (var name in LocalFiles)
            {
                var source = Path.Combine(BattleConfiguration.LocalPath, name);
                if (File.Exists(source)) AddFile(source, "local/" + name, files, ref size);
            }
            var registry = RegistrySession.Capture();
            if (requireLogin && (!registry.Values.Any(v => !string.IsNullOrEmpty(v.Data)) || !files.Keys.Any(p => p.StartsWith("local/", StringComparison.Ordinal))))
                throw new InvalidOperationException("当前登录状态不完整。请在战网勾选保持登录，完成登录后再保存。");
            return new(new(1, DateTimeOffset.Now, files.ContainsKey("config.json"), login?.Identity, login?.Region, BattleConfiguration.ClientDirectory(launcher)), registry, files);
        }
        catch
        {
            foreach (var bytes in files.Values) CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    private static void CaptureDirectory(string directory, string relative, Dictionary<string, byte[]> files, ref long size)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("战网登录目录含有目录链接，无法安全保存。");
        foreach (var file in Directory.EnumerateFiles(directory)) AddFile(file, relative + "/" + Path.GetFileName(file), files, ref size);
        foreach (var child in Directory.EnumerateDirectories(directory)) CaptureDirectory(child, relative + "/" + Path.GetFileName(child), files, ref size);
    }

    private static void AddFile(string path, string relative, Dictionary<string, byte[]> files, ref long size)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("战网登录文件含有链接，无法安全保存。");
        size += info.Length;
        if (size > MaxBytes || files.Count >= MaxFiles) throw new InvalidOperationException("战网缓存超过单个快照 512 MB / 30000 个文件的限制，请先在官方客户端清理缓存。");
        var data = File.ReadAllBytes(path);
        if (data.LongLength != info.Length) throw new IOException("战网登录文件仍在变化，请等待客户端退出后重试。");
        files.Add(relative, data);
    }

    public static void Save(string path, SessionImage image, SessionManifest? manifest = null)
    {
        AccountStore.WriteAtomic(path, output =>
        {
            var entropy = RandomNumberGenerator.GetBytes(32);
            output.Write(Header);
            output.Write(entropy);
            var key = RandomNumberGenerator.GetBytes(32);
            ProtectedChunkStream encrypted;
            try
            {
                var wrappedKey = ProtectedData.Protect(key, entropy, DataProtectionScope.CurrentUser);
                using var header = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
                header.Write(wrappedKey.Length);
                header.Write(wrappedKey);
                encrypted = new(output, writing: true, key);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            using var encryptedLifetime = encrypted;
            using var compressed = new BrotliStream(encrypted, CompressionLevel.Fastest, leaveOpen: true);
            using var writer = new BinaryWriter(compressed, Encoding.UTF8, leaveOpen: true);
            WriteJson(writer, manifest ?? image.Manifest);
            WriteJson(writer, image.Registry);
            writer.Write(image.Files.Count);
            foreach (var (name, data) in image.Files)
            {
                writer.Write(name);
                writer.Write(data.Length);
                writer.Write(data);
            }
        });
    }

    private static void WriteJson<T>(BinaryWriter writer, T value)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value, JsonFormat.Options);
        try { writer.Write(data.Length); writer.Write(data); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private static ProtectedChunkStream Open(Stream input)
    {
        if (input.Length > MaxBytes + 32 * 1024 * 1024) throw new InvalidDataException("登录快照超过大小限制。");
        Span<byte> header = stackalloc byte[Header.Length];
        input.ReadExactly(header);
        if (!header.SequenceEqual(Header)) throw new InvalidDataException("登录快照格式无效，请重新登录保存。");
        var entropy = new byte[32];
        input.ReadExactly(entropy);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var length = reader.ReadInt32();
        if (length < 1 || length > 64 * 1024) throw new InvalidDataException("登录快照的加密密钥记录无效。");
        var wrappedKey = new byte[length];
        input.ReadExactly(wrappedKey);
        byte[] key;
        try { key = ProtectedData.Unprotect(wrappedKey, entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException error)
        {
            throw new InvalidDataException("无法解密登录快照。请使用保存它的 Windows 用户和电脑；快照损坏时需要重新登录保存。", error);
        }
        try
        {
            if (key.Length != 32) throw new InvalidDataException("登录快照的加密密钥无效。");
            return new(input, writing: false, key);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static SessionManifest ReadManifest(string path)
    {
        using var input = File.OpenRead(path);
        using var encrypted = Open(input);
        using var compressed = new BrotliStream(encrypted, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(compressed, Encoding.UTF8, leaveOpen: true);
        var manifest = ReadJson<SessionManifest>(reader);
        if (manifest.Version != 1) throw new InvalidDataException("登录快照版本不受支持。");
        return manifest;
    }

    public static SessionImage Load(string path)
    {
        using var input = File.OpenRead(path);
        using var encrypted = Open(input);
        using var compressed = new BrotliStream(encrypted, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(compressed, Encoding.UTF8, leaveOpen: true);
        Dictionary<string, byte[]> files = [];
        try
        {
            var manifest = ReadJson<SessionManifest>(reader);
            if (manifest.Version != 1) throw new InvalidDataException("登录快照版本不受支持。");
            var registry = ReadJson<RegistryImage>(reader);
            RegistrySession.Validate(registry);
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxFiles) throw new InvalidDataException("登录快照的文件数量无效。");
            long total = 0;
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                ValidatePath(name);
                var length = reader.ReadInt32();
                if (length < 0 || (total += length) > MaxBytes) throw new InvalidDataException("登录快照超过大小限制。");
                var data = new byte[length];
                try
                {
                    compressed.ReadExactly(data);
                    if (!files.TryAdd(name, data)) throw new InvalidDataException("登录快照包含重复文件。");
                }
                catch { CryptographicOperations.ZeroMemory(data); throw; }
            }
            if (compressed.ReadByte() != -1) throw new InvalidDataException("登录快照含有多余内容。");
            encrypted.EnsureComplete();
            if (manifest.HasConfig != files.ContainsKey("config.json")) throw new InvalidDataException("登录快照的配置记录不完整。");
            if (manifest.Identity != null)
            {
                if (string.IsNullOrWhiteSpace(manifest.ClientDirectory)) throw new InvalidDataException("登录快照缺少客户端目录。");
                var login = BattleConfiguration.ReadLogin(files["config.json"], Path.Combine(manifest.ClientDirectory, "Battle.net.exe"));
                if (login.Identity != manifest.Identity || login.Region != manifest.Region)
                    throw new InvalidDataException("登录快照的账号或区服与配置不一致。");
            }
            return new(manifest, registry, files);
        }
        catch
        {
            foreach (var bytes in files.Values) CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    private static T ReadJson<T>(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 1 || length > 16 * 1024 * 1024) throw new InvalidDataException("登录快照的元数据大小无效。");
        var data = new byte[length];
        try
        {
            reader.BaseStream.ReadExactly(data);
            return JsonSerializer.Deserialize<T>(data, JsonFormat.Options) ?? throw new InvalidDataException("登录快照内容为空。");
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private static void ValidatePath(string relative)
    {
        if (relative == "config.json") return;
        if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("登录快照含有无效路径。");
        if (LocalFiles.Any(f => relative == "local/" + f) || LocalDirectories.Any(d => relative.StartsWith("local/" + d + "/", StringComparison.Ordinal))) return;
        throw new InvalidDataException("登录快照含有非战网登录文件。");
    }

    public static void ClearLocalState()
    {
        ValidateLocalRoot();
        foreach (var name in LocalDirectories)
        {
            var path = Path.Combine(BattleConfiguration.LocalPath, name);
            if (Directory.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("战网目录含有链接，已停止切换。");
                Directory.Delete(path, recursive: true);
            }
        }
        foreach (var name in LocalFiles) File.Delete(Path.Combine(BattleConfiguration.LocalPath, name));
        File.Delete(BattleConfiguration.ConfigPath);
    }

    private static void ValidateLocalRoot()
    {
        if (Directory.Exists(BattleConfiguration.LocalPath) && (File.GetAttributes(BattleConfiguration.LocalPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("战网登录目录是链接，无法安全切换。请先检查本机 Battle.net 数据目录。");
        var roaming = Path.GetDirectoryName(BattleConfiguration.ConfigPath)!;
        if (Directory.Exists(roaming) && (File.GetAttributes(roaming) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("战网配置目录是链接，无法安全切换。请先检查本机 Battle.net 配置目录。");
    }

    public static void Restore(SessionImage image)
    {
        RegistrySession.Validate(image.Registry);
        foreach (var relative in image.Files.Keys) ValidatePath(relative);
        ClearLocalState();
        foreach (var (relative, data) in image.Files)
        {
            var destination = relative == "config.json" ? BattleConfiguration.ConfigPath
                : Path.Combine(BattleConfiguration.LocalPath, relative["local/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            AccountStore.WriteAtomic(destination, data);
        }
        RegistrySession.Restore(image.Registry);
    }
}
