using System.Globalization;
using System.Text;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Containers;

/// <summary>
/// Reads the body of Docker's logs endpoint. Without a TTY, Docker frames the output: an 8-byte header
/// (the stream, 1 for stdout or 2 for stderr, three zero bytes, then the length) before each chunk. With a
/// TTY the output comes as it is, all on stdout. Asked for timestamps, each line starts with one.
/// </summary>
internal static class DockerLogs
{
    private const int HeaderLength = 8;

    /// <summary>The newest <paramref name="tail"/> lines, and whether any were left out.</summary>
    public static (List<ContainerLogLine> Lines, bool Truncated) Parse(ReadOnlySpan<byte> body, bool tty, int tail, bool bodyCut)
    {
        var lines = new List<ContainerLogLine>();
        if (tty)
        {
            AddLines(lines, "stdout", body);
        }
        else
        {
            var partial = new Dictionary<string, List<byte>>(StringComparer.Ordinal);
            while (body.Length >= HeaderLength)
            {
                var stream = body[0] == 2 ? "stderr" : "stdout";
                var length = (int)Math.Min(body.Length - HeaderLength, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body[4..HeaderLength]));
                var chunk = body.Slice(HeaderLength, length);
                body = body[(HeaderLength + length)..];

                // A line can be split across chunks of the same stream.
                if (!partial.TryGetValue(stream, out var pending))
                {
                    partial[stream] = pending = [];
                }

                pending.AddRange(chunk);
                var joined = pending.ToArray();
                var end = Array.LastIndexOf(joined, (byte)'\n');
                if (end >= 0)
                {
                    AddLines(lines, stream, joined.AsSpan(0, end + 1));
                    pending.Clear();
                    pending.AddRange(joined.AsSpan(end + 1));
                }
            }

            foreach (var (stream, rest) in partial.Where(pair => pair.Value.Count > 0))
            {
                AddLines(lines, stream, rest.ToArray());
            }

            // Chunks of the two streams interleave by time, which the timestamps restore.
            if (lines.All(line => line.Time is not null))
            {
                lines = [.. lines.OrderBy(line => line.Time)];
            }
        }

        var truncated = bodyCut || lines.Count > tail;
        return (lines.Count > tail ? lines.GetRange(lines.Count - tail, tail) : lines, truncated);
    }

    private static void AddLines(List<ContainerLogLine> lines, string stream, ReadOnlySpan<byte> text)
    {
        foreach (var raw in Encoding.UTF8.GetString(text).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            DateTimeOffset? time = null;
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0 && DateTimeOffset.TryParse(
                    TrimNanoseconds(line[..space]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                time = parsed.ToUniversalTime();
                line = line[(space + 1)..];
            }

            lines.Add(new ContainerLogLine
            {
                Time = time,
                Stream = stream,
                Text = line.Length > AgentLimits.MaxLogLineLength ? line[..AgentLimits.MaxLogLineLength] : line,
            });
        }
    }

    /// <summary>Docker's timestamps have nanoseconds, two digits more than .NET reads.</summary>
    private static string TrimNanoseconds(string timestamp)
    {
        var dot = timestamp.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return timestamp;
        }

        var digits = timestamp.AsSpan(dot + 1).IndexOfAnyExceptInRange('0', '9');
        return digits > 7 ? string.Concat(timestamp.AsSpan(0, dot + 8), timestamp.AsSpan(dot + 1 + digits)) : timestamp;
    }
}
