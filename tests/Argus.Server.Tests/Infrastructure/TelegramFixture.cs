using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Argus.Server.Features.Notifications;
using Argus.Server.Features.Telegram;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>An alerts server whose Telegram bot talks to a fake Bot API, with updates handed in by the tests.</summary>
public sealed class TelegramFixture(PostgresFixture postgres) : AlertsFixture(postgres)
{
    public const string PublicUrl = "https://argus.test";
    public const string Token = "123456789:AAH4-test-token-0123456789abcdefghijk";

    public FakeTelegram Telegram { get; } = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Argus:PublicUrl"] = PublicUrl,
        ["Argus:Telegram:ApiUrl"] = FakeTelegram.ApiUrl,
        ["Argus:Telegram:BackgroundPolling"] = "false",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddHttpClient(TelegramApi.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Telegram);
    }

    public Task<DispatchResult> DispatchAsync() =>
        WithScopeAsync(services => services.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(TestContext.Current.CancellationToken));

    public Task<int> HandleAsync(TelegramUpdate update) =>
        WithScopeAsync(async services =>
        {
            var bot = await services.GetRequiredService<TelegramSettingsStore>().CurrentAsync(TestContext.Current.CancellationToken);
            await services.GetRequiredService<TelegramUpdateHandler>().HandleAsync(bot!, update, TestContext.Current.CancellationToken);
            return 0;
        });
}

/// <summary>Answers Bot API calls the way Telegram does, and keeps each call's method and body.</summary>
public sealed class FakeTelegram : HttpMessageHandler
{
    public const string ApiUrl = "https://telegram.test";

    private readonly List<(string Token, string Method, JsonObject Body)> _calls = [];

    /// <summary>When set, every call fails with this error code and description, as Telegram reports them.</summary>
    public (int Code, string Description)? FailWith { get; set; }

    public IReadOnlyList<JsonObject> Calls(string method)
    {
        lock (_calls)
        {
            return [.. _calls.Where(call => call.Method == method).Select(call => call.Body)];
        }
    }

    public IReadOnlyList<string> Tokens()
    {
        lock (_calls)
        {
            return [.. _calls.Select(call => call.Token)];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // https://telegram.test/bot<token>/<method>
        var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        var token = segments[0]["bot".Length..];
        var method = segments[1];
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        lock (_calls)
        {
            _calls.Add((token, method, body));
        }

        if (FailWith is var (code, description))
        {
            return Answer((HttpStatusCode)code, new JsonObject { ["ok"] = false, ["error_code"] = code, ["description"] = description });
        }

        JsonNode result = method switch
        {
            "getMe" => new JsonObject { ["id"] = 42, ["is_bot"] = true, ["first_name"] = "Argus alerts", ["username"] = "argus_test_bot" },
            "sendMessage" => new JsonObject
            {
                ["message_id"] = 7,
                ["chat"] = new JsonObject { ["id"] = long.Parse(body["chat_id"]!.ToString()), ["type"] = "private" },
                ["text"] = body["text"]!.ToString(),
            },
            "getUpdates" => new JsonArray(),
            _ => JsonValue.Create(true),
        };
        return Answer(HttpStatusCode.OK, new JsonObject { ["ok"] = true, ["result"] = result });
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
