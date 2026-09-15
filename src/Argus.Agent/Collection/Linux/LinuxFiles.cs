namespace Argus.Agent.Collection.Linux;

internal static class LinuxFiles
{
    /// <summary>Reads a /proc or /sys file, treating a missing or unreadable file as empty.</summary>
    public static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>Names of the entries in a /sys directory that have a <c>device</c> link, i.e. real hardware.</summary>
    public static HashSet<string> WithDevice(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFileSystemEntries(directory)
            .Where(entry => Directory.Exists(Path.Combine(entry, "device")))
            .Select(entry => Path.GetFileName(entry))
            .ToHashSet(StringComparer.Ordinal);
    }
}
