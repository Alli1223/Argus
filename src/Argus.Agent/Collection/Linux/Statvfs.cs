using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Argus.Agent.Collection.Linux;

/// <summary>
/// Best-effort inode counts via statvfs(3). Sizes come from <see cref="DriveInfo"/>; inodes are a bonus,
/// so any trouble loading libc simply leaves them out.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class Statvfs
{
    private static bool _unavailable;

    static Statvfs() => NativeLibrary.SetDllImportResolver(typeof(Statvfs).Assembly, ResolveLibc);

    public static (long? Total, long? Used) TryGetInodes(string path)
    {
        if (_unavailable || !Environment.Is64BitProcess)
        {
            return (null, null);
        }

        try
        {
            // Filesystems without a fixed inode table (btrfs, zfs) report zero.
            if (NativeStatvfs(path, out var buffer) != 0 || buffer.Files == 0)
            {
                return (null, null);
            }

            return ((long)buffer.Files, (long)(buffer.Files - Math.Min(buffer.FilesFree, buffer.Files)));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            return (null, null);
        }
    }

    [LibraryImport("libc", EntryPoint = "statvfs", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int NativeStatvfs(string path, out StatvfsBuffer buffer);

    /// <summary>"libc" is not a file name on glibc systems (the library is libc.so.6), so resolve it explicitly.</summary>
    private static IntPtr ResolveLibc(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "libc")
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in (string[])["libc.so.6", "libc.so", "libc"])
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

#pragma warning disable CS0649 // Written by native code.
    /// <summary>struct statvfs on 64-bit Linux; glibc and musl agree on the fields read here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StatvfsBuffer
    {
        public ulong BlockSize;
        public ulong FragmentSize;
        public ulong Blocks;
        public ulong BlocksFree;
        public ulong BlocksAvailable;
        public ulong Files;
        public ulong FilesFree;
        public ulong FilesAvailable;
        public ulong FileSystemId;
        public ulong Flags;
        public ulong NameMax;
        public ulong Spare1;
        public ulong Spare2;
        public ulong Spare3;
        public ulong Spare4;
    }
#pragma warning restore CS0649
}
