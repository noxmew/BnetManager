using System.Text.Json;
using System.Runtime;

namespace BnetManager.Core;

public sealed class AccountManager
{
    private readonly SemaphoreSlim operation = new(1, 1);
    public AccountStore Store { get; } = new();
    public BattleNetProcess Client { get; } = new();
    public AccountCatalog Catalog { get; private set; }
    public bool NeedsRecovery => File.Exists(Store.PendingPath);

    public AccountManager()
    {
        Catalog = Store.Load();
        if (Catalog.LauncherPath == null)
        {
            Catalog.LauncherPath = BattleNetProcess.DiscoverLauncher();
            Store.Save(Catalog);
        }
    }

    private void EnsureReady()
    {
        if (NeedsRecovery) throw new InvalidOperationException("上次操作没有完成。请关闭游戏和战网后重新打开 BnetManager。");
    }

    private async Task Exclusive(Func<Task> action)
    {
        if (!await operation.WaitAsync(0).ConfigureAwait(false)) throw new InvalidOperationException("另一个账号操作正在进行，请稍候。");
        try { await action().ConfigureAwait(false); }
        finally
        {
            // Session operations briefly own hundreds of MB of cleared cache buffers.
            // Return that memory when the operation ends instead of retaining it while idle.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            operation.Release();
        }
    }

    public Task SaveCurrentAsync(string name, string battleTag, string group, BattleRegion region, string? existingId, IProgress<string> progress) => Exclusive(async () =>
    {
        EnsureReady();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40) throw new InvalidOperationException("账号备注需要 1～40 个字符。");
        if (battleTag.Length > 80 || group.Length > 40) throw new InvalidOperationException("战网昵称或分组名称过长。");
        var launcher = BattleNetProcess.ValidateLauncher(Catalog.LauncherPath);
        var wasRunning = Client.IsRunning;
        await Client.StopAsync(launcher, progress).ConfigureAwait(false);
        try
        {
            progress.Report("正在加密保存当前账号…");
            using var image = SessionSnapshot.Capture(launcher, requireLogin: true);
            if (image.Manifest.Region != region) throw new InvalidOperationException($"战网当前位于{Regions.Get(image.Manifest.Region!.Value).Name}，与所选区服不一致，请先在战网登录正确区服。");
            var existing = existingId == null ? null : Catalog.Accounts.Single(a => a.Id == existingId);
            if (existing != null && (existing.LoginIdentity != image.Manifest.Identity || existing.Region != region))
                throw new InvalidOperationException("当前战网账号与这条记录不同，请使用「保存当前账号」新增记录。");
            if (Catalog.Accounts.Any(a => a.Id != existingId && a.LoginIdentity == image.Manifest.Identity && a.Region == region))
                throw new InvalidOperationException("这个账号和区服已经保存过，请使用该账号的「更新登录状态」。");
            var profile = new AccountProfile
            {
                Id = existingId ?? Guid.NewGuid().ToString("N"), Name = name.Trim(), BattleTag = battleTag.Trim(),
                Group = string.IsNullOrWhiteSpace(group) ? "常用账号" : group.Trim(), Region = region,
                LoginIdentity = image.Manifest.Identity!,
                LastLaunchedAt = existing?.LastLaunchedAt, SavedAt = image.Manifest.CapturedAt
            };
            SessionSnapshot.Save(Store.SnapshotPath(profile.Id), image);
            var updated = Catalog with { Accounts = Catalog.Accounts.Where(a => a.Id != profile.Id).Append(profile).ToList() };
            Store.Save(updated);
            Catalog = updated;
        }
        finally
        {
            if (wasRunning) BattleNetProcess.Start(launcher);
        }
    });

    public Task SwitchAsync(string id, IProgress<string> progress) => Exclusive(async () =>
    {
        EnsureReady();
        var profile = Catalog.Accounts.Single(a => a.Id == id);
        var preflight = SessionSnapshot.ReadManifest(Store.SnapshotPath(id));
        if (preflight.Identity != profile.LoginIdentity || preflight.Region != profile.Region)
            throw new InvalidDataException("账号记录与登录快照不一致，请重新保存该账号。");
        if (!string.Equals(preflight.ClientDirectory, BattleConfiguration.ClientDirectory(BattleNetProcess.ValidateLauncher(Catalog.LauncherPath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("这个账号的登录状态来自另一个战网安装目录。请选回原客户端，或在当前客户端重新登录保存。");
        await TransitionAsync(() =>
        {
            using var target = SessionSnapshot.Load(Store.SnapshotPath(id));
            if (target.Manifest.Identity != profile.LoginIdentity || target.Manifest.Region != profile.Region)
                throw new InvalidDataException("账号记录与登录快照不一致，请重新保存该账号。");
            SessionSnapshot.Restore(target);
        }, () =>
        {
            var index = Catalog.Accounts.FindIndex(a => a.Id == id);
            Catalog.Accounts[index] = Catalog.Accounts[index] with { LastLaunchedAt = DateTimeOffset.Now };
            Catalog.LastLaunchedId = id;
        }, progress).ConfigureAwait(false);
    });

    public Task LoginNewAsync(BattleRegion region, IProgress<string> progress) => Exclusive(async () =>
    {
        EnsureReady();
        var launcher = BattleNetProcess.ValidateLauncher(Catalog.LauncherPath);
        if (!File.Exists(BattleConfiguration.ConfigPath)) throw new InvalidOperationException("请先从客户端设置中的安装目录启动一次官方战网，完成初始化后再登录新账号。");
        await TransitionAsync(() =>
        {
            var newConfig = BattleConfiguration.NewLogin(File.ReadAllBytes(BattleConfiguration.ConfigPath), launcher, region);
            SessionSnapshot.ClearLocalState();
            RegistrySession.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(BattleConfiguration.ConfigPath)!);
            AccountStore.WriteAtomic(BattleConfiguration.ConfigPath, newConfig);
        }, () => Catalog.LastLaunchedId = null, progress).ConfigureAwait(false);
    });

    private async Task TransitionAsync(Action apply, Action record, IProgress<string> progress)
    {
        var launcher = BattleNetProcess.ValidateLauncher(Catalog.LauncherPath);
        var catalogBefore = JsonSerializer.SerializeToUtf8Bytes(Catalog, JsonFormat.Options);
        await Client.StopAsync(launcher, progress).ConfigureAwait(false);
        progress.Report("正在准备账号切换…");
        var changed = false;
        try
        {
            using (var previous = SessionSnapshot.Capture(launcher, requireLogin: false))
            {
                SessionSnapshot.Save(Store.PendingPath, previous);
                RefreshLeavingProfile(previous, launcher);
            }
            progress.Report("正在恢复目标账号的登录状态…");
            changed = true;
            apply();
            progress.Report("正在启动战网…");
            BattleNetProcess.Start(launcher);
            record();
            Store.Save(Catalog);
            File.Delete(Store.PendingPath);
        }
        catch (Exception error)
        {
            try
            {
                if (changed)
                {
                    progress.Report("切换未完成，正在恢复切换前状态…");
                    await Client.StopAsync(launcher, progress).ConfigureAwait(false);
                    using var previous = SessionSnapshot.Load(Store.PendingPath);
                    SessionSnapshot.Restore(previous);
                }
                Catalog = JsonSerializer.Deserialize<AccountCatalog>(catalogBefore, JsonFormat.Options)!;
                Store.Save(Catalog);
                File.Delete(Store.PendingPath);
            }
            catch (Exception restoreError)
            {
                throw new AggregateException("切换没有完成。请关闭游戏和战网后重新打开 BnetManager，以完成自动恢复。", error, restoreError);
            }
            throw new InvalidOperationException("切换未完成，切换前状态已恢复。" + Environment.NewLine + error.Message, error);
        }
    }

    private void RefreshLeavingProfile(SessionImage image, string launcher)
    {
        var profile = Catalog.Accounts.SingleOrDefault(a => a.Id == Catalog.LastLaunchedId);
        if (profile == null || !image.Files.TryGetValue("config.json", out var config)) return;
        var root = System.Text.Json.Nodes.JsonNode.Parse(config);
        if (string.IsNullOrWhiteSpace(root?["Client"]?["SavedAccountNames"]?.GetValue<string>())) return;
        var login = BattleConfiguration.ReadLogin(config, launcher);
        if (login.Identity != profile.LoginIdentity || login.Region != profile.Region) return;
        if (!image.Registry.Values.Any(v => !string.IsNullOrEmpty(v.Data)) || !image.Files.Keys.Any(p => p.StartsWith("local/", StringComparison.Ordinal)))
            throw new InvalidOperationException("当前登录状态不完整，无法更新离开账号的快照。请先在官方战网完成登录。");
        SessionSnapshot.Save(Store.SnapshotPath(profile.Id), image, image.Manifest with { Identity = login.Identity, Region = login.Region });
        var index = Catalog.Accounts.IndexOf(profile);
        Catalog.Accounts[index] = profile with { SavedAt = image.Manifest.CapturedAt };
    }

    public Task RepairInterruptedSwitchAsync(IProgress<string> progress) => Exclusive(async () =>
    {
        using var target = SessionSnapshot.Load(Store.PendingPath);
        var launcher = BattleNetProcess.ValidateLauncher(Catalog.LauncherPath);
        await Client.StopAsync(launcher, progress).ConfigureAwait(false);
        progress.Report("正在完成上次中断的操作…");
        SessionSnapshot.Restore(target);
        if (target.Manifest.HasConfig) BattleNetProcess.Start(launcher);
        Catalog.LastLaunchedId = null;
        Store.Save(Catalog);
        File.Delete(Store.PendingPath);
    });

    public void UpdateProfile(string id, string name, string battleTag, string group)
    {
        var index = Catalog.Accounts.FindIndex(a => a.Id == id);
        if (index < 0 || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 || battleTag.Length > 80 || group.Length > 40)
            throw new InvalidOperationException("账号备注、昵称或分组信息无效。");
        var accounts = Catalog.Accounts.ToList();
        accounts[index] = accounts[index] with { Name = name.Trim(), BattleTag = battleTag.Trim(), Group = string.IsNullOrWhiteSpace(group) ? "常用账号" : group.Trim() };
        var updated = Catalog with { Accounts = accounts };
        Store.Save(updated);
        Catalog = updated;
    }

    public void Delete(string id)
    {
        var updated = Catalog with { Accounts = Catalog.Accounts.Where(a => a.Id != id).ToList(), LastLaunchedId = Catalog.LastLaunchedId == id ? null : Catalog.LastLaunchedId };
        Store.Save(updated);
        Catalog = updated;
        File.Delete(Store.SnapshotPath(id));
    }

    public void SetLauncher(string path)
    {
        var updated = Catalog with { LauncherPath = BattleNetProcess.ValidateLauncher(path) };
        Store.Save(updated);
        Catalog = updated;
    }
}
