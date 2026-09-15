using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Finds services that should be running but are not.</summary>
internal interface IServiceStatusSource
{
    /// <summary>The problems found, or null when this machine offers no way to tell.</summary>
    IReadOnlyList<ServiceProblem>? Collect();
}

/// <summary>For systems whose services the agent cannot inspect: reports nothing.</summary>
internal sealed class NoServiceStatus : IServiceStatusSource
{
    public IReadOnlyList<ServiceProblem>? Collect() => null;
}
