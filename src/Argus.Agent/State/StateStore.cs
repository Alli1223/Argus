using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argus.Agent.State;

/// <summary>What the agent must remember between restarts: who it is and how to prove it.</summary>
internal sealed record AgentState
{
    public required Guid HostId { get; init; }

    public required string AgentKey { get; init; }

    /// <summary>The server the key belongs to; pointing the agent at another server re-registers it.</summary>
    public required string ServerUrl { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AgentState))]
internal sealed partial class AgentStateJsonContext : JsonSerializerContext;

/// <summary>
/// Persists <see cref="AgentState"/> in the state directory. The file holds the agent key, so it is
/// written atomically and, on Unix, readable only by the service account (0600 in a 0700 directory).
/// On Windows the installer restricts the directory to SYSTEM and Administrators.
/// </summary>
internal sealed class StateStore(string directory)
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public string Directory { get; } = directory;

    public string FilePath => Path.Combine(Directory, "state.json");

    /// <summary>Returns the saved state, or null when the agent has not registered (or the file is unreadable).</summary>
    public AgentState? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), AgentStateJsonContext.Default.AgentState);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(AgentState state) =>
        WritePrivate(FilePath, JsonSerializer.SerializeToUtf8Bytes(state, AgentStateJsonContext.Default.AgentState));

    public void Delete() => File.Delete(FilePath);

    /// <summary>Reads a small private file from the state directory, or creates it with <paramref name="create"/>.</summary>
    public string GetOrCreate(string fileName, Func<string> create)
    {
        var path = Path.Combine(Directory, fileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }

        var value = create();
        WritePrivate(path, System.Text.Encoding.UTF8.GetBytes(value));
        return value;
    }

    private void WritePrivate(string path, byte[] contents)
    {
        EnsureDirectory();

        // Write to a temporary file and rename it over the old one, so a crash never leaves half a file.
        var temporary = path + ".tmp";
        File.Delete(temporary);

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }

        using (var stream = new FileStream(temporary, options))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        else
        {
            System.IO.Directory.CreateDirectory(Directory, PrivateDirectory);
        }
    }
}
