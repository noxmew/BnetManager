using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BnetManager.Core;

public sealed class BattleNetProcess
{
    private static readonly string[] Clients = ["Battle.net", "Battle.net Launcher", "Battle.net Helper"];
    private static readonly string[] Games = ["Wow", "WowClassic", "WowB", "Overwatch", "Diablo IV", "Diablo III64", "D2R", "Hearthstone", "SC2_x64", "HeroesOfTheStorm_x64"];
    public bool IsRunning => Clients.Any(name => GetProcessIds(name).Count > 0);

    private static HashSet<int> GetProcessIds(string name)
    {
        HashSet<int> ids = [];
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process) ids.Add(process.Id);
        }
        return ids;
    }

    public static void EnsureGamesClosed()
    {
        foreach (var name in Games)
            if (GetProcessIds(name).Count > 0)
                throw new InvalidOperationException($"检测到 {name} 正在运行。请先退出游戏，再操作战网登录状态。");
    }

    public static string? DiscoverLauncher()
    {
        List<string> candidates = [];
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net");
            if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                candidates.Add(Path.Combine(location, "Battle.net.exe"));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    public static string ValidateLauncher(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("尚未找到战网客户端，请在「客户端设置」中选择 Battle.net.exe。");
        var name = Path.GetFileName(path);
        if (!name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) && !name.Equals("Battle.net Launcher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择官方战网的 Battle.net.exe 或 Battle.net Launcher.exe。");
        return Path.GetFullPath(path);
    }

    public async Task StopAsync(string launcher, IProgress<string> progress)
    {
        EnsureGamesClosed();
        progress.Report("正在正常退出战网，保留登录状态…");
        var deadline = DateTime.UtcNow.AddSeconds(25);
        DateTime? quietSince = null;
        Exception? exitFailure = null;
        HashSet<nint> notified = [];
        while (DateTime.UtcNow < deadline)
        {
            var ids = Clients.SelectMany(GetProcessIds).ToHashSet();
            if (ids.Count == 0)
            {
                quietSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - quietSince >= TimeSpan.FromSeconds(2)) break;
            }
            else
            {
                quietSince = null;
                WindowCallback callback = (window, parameter) =>
                {
                    GetWindowThreadProcessId(window, out var pid);
                    if (!ids.Contains((int)pid) || !notified.Add(window)) return true;
                    var query = SendMessageTimeout(window, 0x0011, 0, 0, 0x0002, 1000, out var accepted);
                    if (query == 0 || accepted == 0)
                    {
                        exitFailure = new InvalidOperationException("战网未响应退出请求。请从战网托盘菜单正常退出，然后重试。");
                        return true;
                    }
                    if (SendMessageTimeout(window, 0x0016, 1, 0, 0x0002, 1000, out _) == 0)
                        exitFailure = new InvalidOperationException("战网未能正常结束会话。请手动退出战网后重试。");
                    return true;
                };
                if (!EnumWindows(callback, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法枚举战网窗口。");
            }
            await Task.Delay(400).ConfigureAwait(false);
        }
        if (IsRunning) throw exitFailure ?? new TimeoutException("战网在 25 秒内没有正常退出。请从托盘菜单退出战网后重试。");

        progress.Report("正在等待战网更新进程释放文件…");
        var agentRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Battle.net", "Agent") + Path.DirectorySeparatorChar;
        var installRoot = Path.GetDirectoryName(launcher)! + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName("Agent"))
        {
            using (process)
            {
                if (process.HasExited) continue;
                var executable = process.MainModule?.FileName ?? throw new IOException("无法核对 Agent 进程的来源，请手动退出战网更新服务。");
                if (!executable.StartsWith(agentRoot, StringComparison.OrdinalIgnoreCase) && !executable.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
        }
        await Task.Delay(600).ConfigureAwait(false);
        if (IsRunning) throw new IOException("战网客户端已重新启动，请先关闭自动启动它的程序。");
        EnsureGamesClosed();
    }

    public static void Start(string launcher)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = ValidateLauncher(launcher), UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launcher) })
            ?? throw new IOException("Windows 未能启动战网客户端。");
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
