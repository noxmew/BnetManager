using System.IO;
using System.Text.Json;

namespace BnetManager.Core;

public sealed class AccountStore
{
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BnetManager");
    public string PendingPath => Path.Combine(Root, "pending.session");
    private string CatalogPath => Path.Combine(Root, "accounts.json");

    public AccountStore() => Directory.CreateDirectory(Root);

    public AccountCatalog Load()
    {
        if (!File.Exists(CatalogPath)) return new();
        var catalog = JsonSerializer.Deserialize<AccountCatalog>(File.ReadAllBytes(CatalogPath), JsonFormat.Options)
            ?? throw new InvalidDataException("账号库内容为空，请检查本机数据目录。");
        if (catalog.Version != 1) throw new InvalidDataException($"不支持账号库版本 {catalog.Version}。");
        if (catalog.Accounts.Any(a => !Guid.TryParseExact(a.Id, "N", out _) || string.IsNullOrWhiteSpace(a.Name) || !Enum.IsDefined(a.Region))
            || catalog.Accounts.Select(a => a.Id).Distinct().Count() != catalog.Accounts.Count)
            throw new InvalidDataException("账号库包含无效或重复的账号记录，请检查本机数据目录。");
        return catalog;
    }

    public void Save(AccountCatalog catalog) => WriteAtomic(CatalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, JsonFormat.Options));

    public string SnapshotPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("账号标识无效。");
        var path = Path.Combine(Root, "sessions");
        Directory.CreateDirectory(path);
        return Path.Combine(path, id + ".session");
    }

    public static void WriteAtomic(string path, byte[] data) => WriteAtomic(path, stream => stream.Write(data));

    public static void WriteAtomic(string path, Action<Stream> write)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
