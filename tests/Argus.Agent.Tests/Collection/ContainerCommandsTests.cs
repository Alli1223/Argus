using System.Net;
using System.Text;
using Argus.Agent.Collection.Containers;
using Argus.Agent.Commands;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Collection;

public sealed class DockerLogsTests
{
    /// <summary>Docker's framing of non-TTY output: stream, three zero bytes, big-endian length, then the bytes.</summary>
    internal static byte[] Frame(int stream, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = (byte)stream;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    [Fact]
    public void Framed_output_becomes_lines_in_time_order_with_their_stream()
    {
        byte[] body =
        [
            .. Frame(1, "2026-09-17T11:00:01.000000001Z starting\n2026-09-17T11:00:03.5Z listening on :80\n"),
            .. Frame(2, "2026-09-17T11:00:02.123456789Z warning: no config, using def"),
            .. Frame(2, "aults\r\n"),
        ];

        var (lines, truncated) = DockerLogs.Parse(body, tty: false, tail: 100, bodyCut: false);

        Assert.False(truncated);
        Assert.Equal(
            ["stdout starting", "stderr warning: no config, using defaults", "stdout listening on :80"],
            lines.Select(line => $"{line.Stream} {line.Text}"));
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 11, 0, 2, TimeSpan.Zero).AddTicks(1_234_567), lines[1].Time);
    }

    [Fact]
    public void Tty_output_is_all_stdout_and_the_tail_keeps_the_newest_lines()
    {
        var body = Encoding.UTF8.GetBytes("2026-09-17T11:00:01Z one\n2026-09-17T11:00:02Z two\n2026-09-17T11:00:03Z three\n");

        var (lines, truncated) = DockerLogs.Parse(body, tty: true, tail: 2, bodyCut: false);

        Assert.True(truncated);
        Assert.Equal(["two", "three"], lines.Select(line => line.Text));
        Assert.All(lines, line => Assert.Equal("stdout", line.Stream));
    }
}

public sealed class ContainerCommandsTests
{
    private sealed class Docker : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return Task.FromResult(Answer(request));
        }
    }

    private static (ContainerCommands Commands, Docker Docker) Create(bool enabled = true)
    {
        var docker = new Docker();
        var commands = new ContainerCommands(
            new AgentConfig { ContainerActions = enabled }, NullLogger<ContainerCommands>.Instance, new DockerClient(docker));
        return (commands, docker);
    }

    private static AgentCommand Command(AgentCommandKind kind, string container = "shop-web-1", int? tail = null) =>
        new() { Id = "c1", Kind = kind, Container = container, Tail = tail };

    private static Task<AgentCommandResult> RunAsync(ContainerCommands commands, AgentCommand command) =>
        commands.RunAsync(command, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Start_stop_and_restart_ask_Docker_for_just_that()
    {
        var (commands, docker) = Create();

        Assert.True((await RunAsync(commands, Command(AgentCommandKind.ContainerStart))).Succeeded);
        Assert.True((await RunAsync(commands, Command(AgentCommandKind.ContainerStop))).Succeeded);
        Assert.True((await RunAsync(commands, Command(AgentCommandKind.ContainerRestart))).Succeeded);

        Assert.Equal(
            ["POST /containers/shop-web-1/start", "POST /containers/shop-web-1/stop?t=10", "POST /containers/shop-web-1/restart?t=10"],
            docker.Requests);
    }

    [Fact]
    public async Task Containers_already_as_asked_count_as_done_and_Docker_errors_are_passed_on()
    {
        var (commands, docker) = Create();
        docker.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        Assert.True((await RunAsync(commands, Command(AgentCommandKind.ContainerStop))).Succeeded);

        docker.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message": "No such container: shop-web-1"}"""),
        };
        var result = await RunAsync(commands, Command(AgentCommandKind.ContainerStart));
        Assert.Equal((false, "No such container: shop-web-1"), (result.Succeeded, result.Error));
    }

    [Theory]
    [InlineData("../../images/json")]
    [InlineData("web?force=1")]
    [InlineData("")]
    [InlineData("-web")]
    public async Task Anything_but_a_container_name_is_refused(string container)
    {
        var (commands, docker) = Create();

        var result = await RunAsync(commands, Command(AgentCommandKind.ContainerStop, container));

        Assert.False(result.Succeeded);
        Assert.Empty(docker.Requests);
    }

    [Fact]
    public async Task Nothing_happens_when_container_actions_are_off()
    {
        var (commands, docker) = Create(enabled: false);

        var result = await RunAsync(commands, Command(AgentCommandKind.ContainerLogs));

        Assert.Equal((false, "Container actions are off on this machine."), (result.Succeeded, result.Error));
        Assert.Empty(docker.Requests);
    }

    [Fact]
    public async Task Logs_come_back_as_lines()
    {
        var (commands, docker) = Create();
        docker.Answer = request => request.RequestUri!.AbsolutePath.EndsWith("/json", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"Config": {"Tty": false}}""") }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. DockerLogsTests.Frame(1, "2026-09-17T11:00:01Z ready\n"), .. DockerLogsTests.Frame(2, "2026-09-17T11:00:02Z oops\n")]),
            };

        var result = await RunAsync(commands, Command(AgentCommandKind.ContainerLogs, tail: 50));

        Assert.True(result.Succeeded);
        Assert.Equal(["stdout ready", "stderr oops"], result.Logs!.Select(line => $"{line.Stream} {line.Text}"));
        Assert.Contains("GET /containers/shop-web-1/logs?stdout=1&stderr=1&timestamps=1&tail=50", docker.Requests);
    }
}
