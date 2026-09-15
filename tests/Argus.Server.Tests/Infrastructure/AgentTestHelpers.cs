using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Agents;
using Argus.Server.Tests.Enrollment;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>JSON options matching the server's (camelCase, enums as strings).</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public static class AgentTestHelpers
{
    public const string Password = "correct horse battery";

    /// <summary>Creates a user and returns a browser-like client signed in as them.</summary>
    public static async Task<HttpClient> CreateOwnerAsync(this ArgusAppFixture app, string email, string role = Roles.User)
    {
        await app.CreateUserAsync(email, Password, role);
        return await app.CreateSignedInClientAsync(email, Password);
    }

    /// <summary>Enrolls a host for <paramref name="owner"/> and returns an agent client authenticated with its key.</summary>
    public static async Task<(Guid HostId, HttpClient Agent)> RegisterHostAsync(
        this ArgusAppFixture app, HttpClient owner, string machineId, string hostname = "web-1")
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await EnrollmentTokenTests.CreateTokenAsync(owner);

        var response = await app.Factory.CreateClient().PostAsJsonAsync(
            AgentApi.Register, AgentRegistrationTests.Registration(token.Token, machineId, hostname),
            AgentJsonContext.Default.RegisterAgentRequest, ct);
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.RegisterAgentResponse, ct))!;

        var agent = app.Factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AgentKey);
        return (registered.HostId, agent);
    }

    public static async Task SendSamplesAsync(this HttpClient agent, params MetricSample[] samples)
    {
        var response = await agent.PostAsJsonAsync(
            AgentApi.Metrics, new MetricsBatch { Samples = samples }, AgentJsonContext.Default.MetricsBatch,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string url) =>
        client.GetFromJsonAsync<T>(url, TestJson.Options, TestContext.Current.CancellationToken);

    /// <summary>ISO 8601 timestamp, escaped for use in a query string.</summary>
    public static string Iso(DateTimeOffset time) => Uri.EscapeDataString(time.ToUniversalTime().ToString("O"));
}
