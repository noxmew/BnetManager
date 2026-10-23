using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace BnetManager.Core;

public sealed record RegistryValue(string Path, string Name, RegistryValueKind Kind, string Data);
public sealed record RegistryImage(List<string> Keys, List<RegistryValue> Values);

public static class RegistrySession
{
    private const string Base = @"Software\Blizzard Entertainment\Battle.net\";
    private static readonly string[] Trees = [Base + "UnifiedAuth", Base + "Identity", Base + "EncryptionKey"];
    private const string LaunchOptions = Base + "Launch Options";

    public static RegistryImage Capture()
    {
        List<string> keys = [];
        List<RegistryValue> values = [];
        foreach (var path in Trees) ReadTree(path, keys, values);
        using var launch = Registry.CurrentUser.OpenSubKey(LaunchOptions);
        if (launch?.GetValueNames().Contains("URI_TOKEN", StringComparer.OrdinalIgnoreCase) == true)
            values.Add(ReadValue(launch, LaunchOptions, "URI_TOKEN"));
        return new(keys, values);
    }

    private static void ReadTree(string path, List<string> keys, List<RegistryValue> values)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key == null) return;
        keys.Add(path);
        foreach (var name in key.GetValueNames()) values.Add(ReadValue(key, path, name));
        foreach (var child in key.GetSubKeyNames()) ReadTree(path + "\\" + child, keys, values);
    }

    private static RegistryValue ReadValue(RegistryKey key, string path, string name)
    {
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new IOException("读取战网认证状态时注册表发生变化，请关闭战网后重试。");
        var data = kind switch
        {
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
            RegistryValueKind.DWord => ((int)value).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => ((long)value).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
            RegistryValueKind.String or RegistryValueKind.ExpandString => (string)value,
            _ => throw new InvalidDataException("战网认证状态包含不支持的注册表类型。")
        };
        return new(path, name, kind, data);
    }

    public static void Validate(RegistryImage image)
    {
        foreach (var path in image.Keys)
            if (!IsTreePath(path)) throw new InvalidDataException("登录快照含有不允许的注册表路径。");
        foreach (var value in image.Values)
        {
            if (!IsTreePath(value.Path) && !(value.Path == LaunchOptions && value.Name == "URI_TOKEN"))
                throw new InvalidDataException("登录快照含有不允许的注册表值。");
            _ = Decode(value);
        }
    }

    private static bool IsTreePath(string path) => Trees.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));

    private static object Decode(RegistryValue value) => value.Kind switch
    {
        RegistryValueKind.Binary => Convert.FromBase64String(value.Data),
        RegistryValueKind.DWord => int.Parse(value.Data, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value.Data, CultureInfo.InvariantCulture),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Data) ?? throw new InvalidDataException("注册表数组为空。"),
        RegistryValueKind.String or RegistryValueKind.ExpandString => value.Data,
        _ => throw new InvalidDataException("快照中的注册表类型无效。")
    };

    public static void Clear()
    {
        foreach (var path in Trees) Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        using var launch = Registry.CurrentUser.OpenSubKey(LaunchOptions, writable: true);
        launch?.DeleteValue("URI_TOKEN", throwOnMissingValue: false);
    }

    public static void Restore(RegistryImage image)
    {
        Validate(image);
        Clear();
        foreach (var path in image.Keys)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        }
        foreach (var value in image.Values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(value.Path, writable: true);
            key.SetValue(value.Name, Decode(value), value.Kind);
        }
    }
}
