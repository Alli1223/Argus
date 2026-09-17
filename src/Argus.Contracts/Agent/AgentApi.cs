namespace Argus.Contracts.Agent;

/// <summary>Routes and constants of the agent ↔ server protocol.</summary>
public static class AgentApi
{
    public const int ProtocolVersion = 1;

    public const string BasePath = "/api/agent/v1";
    public const string Register = BasePath + "/register";
    public const string Metrics = BasePath + "/metrics";
    public const string Inventory = BasePath + "/inventory";

    /// <summary>The update the server wants this agent to install, if any (<see cref="AgentUpdateOffer"/>).</summary>
    public const string UpdateOffer = BasePath + "/update/offer";

    /// <summary>The offered agent build itself.</summary>
    public const string UpdateDownload = BasePath + "/update/download";

    /// <summary>Where an agent reports how an update went (<see cref="AgentUpdateResult"/>).</summary>
    public const string UpdateResult = BasePath + "/update/result";

    /// <summary>Prefix of per-host agent keys (sent as <c>Authorization: Bearer …</c>).</summary>
    public const string AgentKeyPrefix = "argus_ak_";

    /// <summary>Prefix of enrollment tokens created in the UI.</summary>
    public const string EnrollmentTokenPrefix = "argus_et_";
}

/// <summary>Upper bounds the server enforces on agent payloads.</summary>
public static class AgentLimits
{
    public const int MaxSamplesPerBatch = 500;
    public const int MaxFilesystemsPerSample = 64;
    public const int MaxInterfacesPerSample = 64;
    public const int MaxTemperaturesPerSample = 256;
    public const int MaxContainers = 500;
    public const int MaxContainerPorts = 32;
    public const int MaxTopProcesses = 50;
    public const int MaxFailedServices = 100;
    public const int MaxIpAddresses = 32;
    public const int MaxNameLength = 256;
}
