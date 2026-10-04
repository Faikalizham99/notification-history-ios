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
    // Darwin open() is variadic: its mode argument uses the stack on Apple Silicon.
    // creat() has a fixed signature and opens the same empty lock inode on every call.
    [DllImport(SystemLibrary, EntryPoint = "creat", SetLastError = true)]
    private static extern int Create([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ushort mode);
    [DllImport(SystemLibrary, EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
    [DllImport(SystemLibrary, EntryPoint = "close")]
    private static extern int Close(int descriptor);

    public static SharedWriterLock? Acquire(string path, CancellationToken token)
    {
        if (!OperatingSystem.IsIOS() && !OperatingSystem.IsMacOS()) return null;
        // Owner read/write (0600). Truncation is safe: this file holds no data;
        // flock locks the inode, which creat preserves when the file already exists.
        var descriptor = Create(path + ".lock", 0x0180);
        if (descriptor < 0)
            throw new IOException($"Cannot access the shared history lock (errno {Marshal.GetLastPInvokeError()}).");
        try
        {
            var timer = Stopwatch.StartNew();
            while (Flock(descriptor, 2 | 4) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                token.ThrowIfCancellationRequested();
                // Darwin EINTR (4) and EWOULDBLOCK/EAGAIN (35) can be retried.
                if (error != 4 && error != 35)
                    throw new IOException($"Cannot acquire the shared history lock (errno {error}).");
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
