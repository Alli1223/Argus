using System.CommandLine;
using Argus.Agent;

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to the agent config file",
    Recursive = true,
};
var serverOption = new Option<string?>("--server") { Description = "Server URL, e.g. https://argus.example.com" };
var tokenOption = new Option<string?>("--token") { Description = "Enrollment token created in the Argus web UI" };

var run = new Command("run", "Run the agent (this is what the service starts)");
run.SetAction((parsed, cancellationToken) => AgentCommands.RunAsync(parsed.GetValue(configOption), cancellationToken));

var register = new Command("register", "Register this machine with the server and save its identity")
{
    serverOption,
    tokenOption,
};
register.SetAction((parsed, cancellationToken) => AgentCommands.RegisterAsync(
    parsed.GetValue(configOption), parsed.GetValue(serverOption), parsed.GetValue(tokenOption), cancellationToken));

var collect = new Command("collect", "Collect one sample and print it as JSON, for troubleshooting");
collect.SetAction((_, cancellationToken) => AgentCommands.CollectAsync(cancellationToken));

var version = new Command("version", "Print the agent version");
version.SetAction(_ => Console.WriteLine(AgentInfo.Version));

var root = new RootCommand("Argus monitoring agent") { run, register, collect, version };
root.Options.Add(configOption);

// The host handles Ctrl+C and service stop requests itself (and needs time for a final flush).
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null });
