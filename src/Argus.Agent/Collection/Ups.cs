using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Reads the machine's uninterruptible power supplies.</summary>
internal interface IUpsSource
{
    /// <summary>Every UPS that answered; empty on machines without any the agent can reach.</summary>
    IReadOnlyList<UpsMetrics> Collect();
}

/// <summary>For agents told not to look for a UPS: reports none.</summary>
internal sealed class NoUps : IUpsSource
{
    public IReadOnlyList<UpsMetrics> Collect() => [];
}

/// <summary>upsd answered a command with <c>ERR …</c>, or not at all.</summary>
internal sealed class NutException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads UPSes from a Network UPS Tools server (upsd), which NAS systems and most Linux UPS setups run
/// next to the UPS's driver. Its network protocol is a few lines of text, and reading a UPS's variables
/// needs no login. When upsd cannot be reached the agent waits a minute before asking again, so machines
/// without one cost nothing.
/// </summary>
internal sealed class NutUps(string server, TimeProvider time, ILogger<NutUps> logger) : IUpsSource
{
    public const int DefaultPort = 3493;

    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private bool _reached;

    public IReadOnlyList<UpsMetrics> Collect()
    {
        var now = time.GetUtcNow();
        if (now < _nextAttempt)
        {
            return [];
        }

        try
        {
            var readings = Read();
            if (!_reached)
            {
                logger.LogInformation("Reading {Count} UPS from upsd at {Server}", readings.Count, server);
                _reached = true;
            }

            return readings;
        }
        catch (Exception ex) when (ex is NutException or SocketException or IOException or OperationCanceledException)
        {
            _nextAttempt = now + RetryInterval;

            // Most machines have no upsd, so not finding one is only worth a warning once it has been seen.
            if (_reached)
            {
                logger.LogWarning("Could not read UPS from upsd at {Server}: {Error}", server, ex.Message);
                _reached = false;
            }
            else
            {
                logger.LogDebug("No UPS read from upsd at {Server}: {Error}", server, ex.Message);
            }

            return [];
        }
    }

    private List<UpsMetrics> Read()
    {
        var (host, port) = NutProtocol.ParseServer(server);
        using var client = new TcpClient { ReceiveTimeout = (int)Timeout.TotalMilliseconds, SendTimeout = (int)Timeout.TotalMilliseconds };
        using (var connecting = new CancellationTokenSource(Timeout))
        {
            client.ConnectAsync(host, port, connecting.Token).AsTask().GetAwaiter().GetResult();
        }

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

        var readings = new List<UpsMetrics>();
        foreach (var (name, description) in NutProtocol.ParseUpsList(List(reader, writer, "UPS")))
        {
            try
            {
                var variables = NutProtocol.ParseVariables(List(reader, writer, $"VAR {name}"));
                readings.Add(NutProtocol.ToMetrics(name, description, variables));
            }
            catch (NutException ex)
            {
                // A UPS whose driver lost it (DATA-STALE, DRIVER-NOT-CONNECTED) is skipped; the others still count.
                logger.LogDebug("upsd has no readings for UPS {Ups}: {Error}", name, ex.Message);
            }

            if (readings.Count >= AgentLimits.MaxUpsPerSample)
            {
                break;
            }
        }

        writer.WriteLine("LOGOUT");
        return readings;
    }

    /// <summary>Sends <c>LIST {query}</c> and returns the lines between BEGIN and END.</summary>
    private static List<string> List(StreamReader reader, StreamWriter writer, string query)
    {
        writer.WriteLine($"LIST {query}");
        var lines = new List<string>();
        var begun = false;
        while (true)
        {
            var line = reader.ReadLine() ?? throw new NutException("upsd closed the connection.");
            if (line.StartsWith("ERR ", StringComparison.Ordinal))
            {
                throw new NutException($"upsd answered {line}.");
            }

            if (line.StartsWith("BEGIN LIST ", StringComparison.Ordinal))
            {
                begun = true;
            }
            else if (line.StartsWith("END LIST ", StringComparison.Ordinal))
            {
                return lines;
            }
            else if (begun)
            {
                lines.Add(line);
            }
        }
    }
}

/// <summary>The text of upsd's network protocol, kept free of I/O so it is easy to test.</summary>
internal static class NutProtocol
{
    /// <summary>"host", "host:port", "[::1]:port" or "[::1]".</summary>
    public static (string Host, int Port) ParseServer(string server)
    {
        var value = server.Trim();
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            var host = value[1..close];
            return value.Length > close + 2 && value[close + 1] == ':' ? (host, int.Parse(value[(close + 2)..], CultureInfo.InvariantCulture)) : (host, NutUps.DefaultPort);
        }

        var colon = value.LastIndexOf(':');
        return colon > 0 && value.IndexOf(':') == colon
            ? (value[..colon], int.Parse(value[(colon + 1)..], CultureInfo.InvariantCulture))
            : (value, NutUps.DefaultPort);
    }

    /// <summary>Lines such as <c>UPS ups "Back-UPS ES 700G"</c>: each UPS's name and description.</summary>
    public static List<(string Name, string? Description)> ParseUpsList(IEnumerable<string> lines) =>
        lines.Select(Split)
            .Where(words => words.Count >= 2 && words[0] == "UPS")
            .Select(words => (words[1], words.Count > 2 && words[2].Length > 0 && words[2] != "Unavailable" ? words[2] : null))
            .ToList();

    /// <summary>Lines such as <c>VAR ups battery.charge "100"</c>, by variable name.</summary>
    public static Dictionary<string, string> ParseVariables(IEnumerable<string> lines)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var words in lines.Select(Split))
        {
            if (words.Count >= 4 && words[0] == "VAR")
            {
                variables[words[2]] = words[3];
            }
        }

        return variables;
    }

    public static UpsMetrics ToMetrics(string name, string? description, IReadOnlyDictionary<string, string> variables)
    {
        var maker = Value("device.mfr") ?? Value("ups.mfr");
        var model = Value("device.model") ?? Value("ups.model");
        var fullModel = model is null ? description
            : maker is null || model.Contains(maker, StringComparison.OrdinalIgnoreCase) ? model
            : $"{maker} {model}";

        return new UpsMetrics
        {
            Name = name,
            Model = fullModel,
            Status = Value("ups.status") ?? "",
            BatteryChargePercent = Number("battery.charge"),
            BatteryRuntimeSeconds = Number("battery.runtime"),
            LoadPercent = Number("ups.load"),
            InputVoltage = Number("input.voltage"),
            OutputVoltage = Number("output.voltage"),
            BatteryVoltage = Number("battery.voltage"),
        };

        string? Value(string variable) =>
            variables.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        double? Number(string variable) =>
            Value(variable) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number)
                ? number
                : null;
    }

    /// <summary>Splits a line into words, where a word in double quotes may hold spaces and \" or \\ escapes.</summary>
    public static List<string> Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        var inWord = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < line.Length)
                {
                    word.Append(line[++i]);
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    word.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
                inWord = true;
            }
            else if (c == ' ')
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
