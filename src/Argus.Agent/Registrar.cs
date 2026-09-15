using Argus.Agent.Collection;
using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent;

/// <summary>Exchanges an enrollment token for a host identity and saves it.</summary>
internal sealed class Registrar(
    AgentConfig config,
    StateStore stateStore,
    ISystemInfoSource systemInfo,
    ArgusClient client,
    TimeProvider time,
    ILogger<Registrar> logger)
{
    public async Task<ApiResult<AgentState>> RegisterAsync(string enrollmentToken, CancellationToken cancellationToken)
    {
        var request = new RegisterAgentRequest
        {
            EnrollmentToken = enrollmentToken.Trim(),
            MachineId = MachineId(),
            AgentVersion = AgentInfo.Version,
            SystemInfo = systemInfo.Collect(),
        };

        var result = await client.RegisterAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.As<AgentState>();
        }

        var state = new AgentState
        {
            HostId = result.Value!.HostId,
            AgentKey = result.Value.AgentKey,
            ServerUrl = config.ServerUrl,
            RegisteredAt = time.GetUtcNow(),
        };
        stateStore.Save(state);

        logger.LogInformation("Registered with {Server} as host {HostId}", config.ServerUrl, state.HostId);
        return ApiResult<AgentState>.Success(state);
    }

    /// <summary>
    /// The hashed OS machine id, so reinstalling the agent re-links the same host. Machines without
    /// one get a random id that is kept in the state directory.
    /// </summary>
    private string MachineId() =>
        MachineIdentity.Hash(systemInfo.ReadMachineId() ?? stateStore.GetOrCreate("machine-id", () => Guid.NewGuid().ToString("N")));
}
