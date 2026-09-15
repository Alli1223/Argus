namespace Argus.Contracts.Agent;

/// <summary>Routes and constants of the agent ↔ server protocol.</summary>
public static class AgentApi
{
    public const int ProtocolVersion = 1;

    public const string BasePath = "/api/agent/v1";
    public const string Register = BasePath + "/register";
    public const string Metrics = BasePath + "/metrics";
    public const string Inventory = BasePath + "/inventory";

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
    public const int MaxTopProcesses = 50;
    public const int MaxIpAddresses = 32;
    public const int MaxNameLength = 256;
}
