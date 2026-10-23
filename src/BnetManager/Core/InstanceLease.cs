namespace BnetManager.Core;

public static class InstanceLease
{
    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BnetManager");

    public static FileStream? TryAcquire()
    {
        Directory.CreateDirectory(DataRoot);
        try { return new FileStream(Path.Combine(DataRoot, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return null; }
    }
}
