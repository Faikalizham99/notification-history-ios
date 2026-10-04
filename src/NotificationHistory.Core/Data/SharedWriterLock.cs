using System.Diagnostics;
using System.Runtime.InteropServices;
namespace NotificationHistory.Core.Data;

// Serializes writable connection lifetimes, including close-time WAL checkpoints.
// Raw POSIX descriptors avoid FileStream's separate file-sharing locks on Unix.
internal sealed class SharedWriterLock : IDisposable
{
    private int descriptor;
    private SharedWriterLock(int descriptor) { this.descriptor = descriptor; }
#if IOS
    private const string SystemLibrary = "__Internal";
#else
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
#endif
    [DllImport(SystemLibrary, EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport(SystemLibrary, EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
    [DllImport(SystemLibrary, EntryPoint = "close")]
    private static extern int Close(int descriptor);

    public static SharedWriterLock? Acquire(string path, CancellationToken token)
    {
        if (!OperatingSystem.IsIOS() && !OperatingSystem.IsMacOS()) return null;
        // Darwin: O_CREAT | O_RDWR, owner read/write (0600).
        var descriptor = Open(path + ".lock", 0x0200 | 2, 0x0180);
        if (descriptor < 0) throw new IOException("Cannot access the shared history lock.");
        try
        {
            var timer = Stopwatch.StartNew();
            while (Flock(descriptor, 2 | 4) != 0)
            {
                token.ThrowIfCancellationRequested();
                if (timer.Elapsed >= TimeSpan.FromSeconds(5)) throw new IOException("Shared history is busy. Please try again.");
                Thread.Sleep(20);
            }
            return new(descriptor);
        }
        catch { Close(descriptor); throw; }
    }
    public void Dispose()
    {
        if (descriptor < 0) return;
        Flock(descriptor, 8);
        Close(descriptor);
        descriptor = -1;
    }
}
