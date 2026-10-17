using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Features.Telegram;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Notifications;

public sealed class TelegramTests(TelegramFixture app) : IClassFixture<TelegramFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => app.Time.GetUtcNow();

    private async Task SaveBotAsync()
    {
        var admin = await app.CreateOwnerAsync($"tg-admin-{Guid.NewGuid():N}@example.com", Roles.Admin);
        (await admin.PutAsJsonAsync("/api/settings/telegram", new { botToken = TelegramFixture.Token }, Ct)).EnsureSuccessStatusCode();
    }

    /// <summary>An owner with a Telegram channel to <paramref name="chatId"/>, a CPU rule and a host whose CPU is hot.</summary>
    private async Task<(Guid OwnerId, HttpClient Owner, Alert Alert)> FiringAlertAsync(string email, long chatId)
    {
        await SaveBotAsync();
        var owner = await app.CreateOwnerAsync(email);
        var ownerId = (await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id;
        (await owner.PostAsJsonAsync("/api/notification-channels",
            new { name = "Phone", kind = "Telegram", target = chatId.ToString() }, Ct)).EnsureSuccessStatusCode();
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, configure: rule => rule.Name = "Hot CPU");
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine", "web-1");
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(Now.AddSeconds(-5)) with { Cpu = new CpuMetrics { UsagePercent = 97 } });
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        return (ownerId, owner, alert);
    }

    private static TelegramUpdate Press(long chatId, string data) => new(
        UpdateId: 1,
        Message: null,
        ChannelPost: null,
        CallbackQuery: new TelegramCallbackQuery(
            Id: Guid.NewGuid().ToString(),
            From: new TelegramUser(500, IsBot: false, "Alice", null, "alice"),
            Message: new TelegramMessage(7, new TelegramChat(chatId, "private", null, null), null, "alert"),
            Data: data));

    private Task<Alert> ReloadAsync(Guid alertId) =>
        app.WithScopeAsync(services => services.GetRequiredService<ArgusDbContext>().Alerts.AsNoTracking().SingleAsync(a => a.Id == alertId, Ct));

    [Fact]
    public async Task Administrators_save_a_bot_that_Telegram_accepts()
    {
        var admin = await app.CreateOwnerAsync("tg-set@example.com", Roles.Admin);
        var user = await app.CreateOwnerAsync("tg-set-user@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/telegram", Ct)).StatusCode);

        var malformed = await admin.PutAsJsonAsync("/api/settings/telegram", new { botToken = "not a token" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        app.Telegram.FailWith = (401, "Unauthorized");
        var rejected = await admin.PutAsJsonAsync("/api/settings/telegram", new { botToken = TelegramFixture.Token }, Ct);
        app.Telegram.FailWith = null;
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.Contains("Unauthorized", problem!.Errors["botToken"][0]);

        var saved = await admin.PutAsJsonAsync("/api/settings/telegram", new { botToken = TelegramFixture.Token }, Ct);
        saved.EnsureSuccessStatusCode();
        var settings = (await saved.Content.ReadFromJsonAsync<TelegramSettingsResponse>(Ct))!;
        Assert.True(settings.Configured);
        Assert.Equal("argus_test_bot", settings.BotUsername);
        Assert.DoesNotContain(TelegramFixture.Token, await saved.Content.ReadAsStringAsync(Ct));

        var support = await user.GetJsonAsync<NotificationSupport>("/api/notification-channels/support");
        Assert.Equal("argus_test_bot", support!.TelegramBot);
    }

    [Fact]
    public async Task Telegram_channels_take_a_chat_id()
    {
        var owner = await app.CreateOwnerAsync("tg-chan@example.com");

        foreach (var good in new[] { "123456789", "-1001234567890", "@argus_alerts" })
        {
            var created = await owner.PostAsJsonAsync("/api/notification-channels", new { name = good, kind = "Telegram", target = good }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var bad = await owner.PostAsJsonAsync("/api/notification-channels", new { name = "x", kind = "Telegram", target = "https://t.me/x" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Firing_alerts_arrive_with_acknowledge_and_ignore_buttons()
    {
        var (_, _, alert) = await FiringAlertAsync("tg-send@example.com", 1001);
        Assert.Equal(1, (await app.DispatchAsync()).Sent);

        var fired = app.Telegram.Calls("sendMessage").Last(call => call["chat_id"]!.ToString() == "1001");
        Assert.Equal("HTML", fired["parse_mode"]!.ToString());
        Assert.Contains("<b>🔴 [Critical] CPU usage on web-1 above 80%</b>", fired["text"]!.ToString());
        Assert.Contains($"{TelegramFixture.PublicUrl}/hosts/{alert.HostId}", fired["text"]!.ToString());
        var buttons = fired["reply_markup"]!["inline_keyboard"]![0]!.AsArray();
        Assert.Equal([$"ack:{alert.Id}", $"ign:{alert.Id}"], buttons.Select(button => button!["callback_data"]!.ToString()));
        Assert.All(app.Telegram.Tokens(), token => Assert.Equal(TelegramFixture.Token, token));
    }

    [Fact]
    public async Task Acknowledge_works_from_the_alerts_own_chat_only()
    {
        var (_, owner, alert) = await FiringAlertAsync("tg-ack@example.com", 1002);

        await app.HandleAsync(Press(9999, $"ack:{alert.Id}"));
        Assert.Null((await ReloadAsync(alert.Id)).AcknowledgedAt);

        await app.HandleAsync(Press(1002, $"ack:{alert.Id}"));
        var acknowledged = await ReloadAsync(alert.Id);
        Assert.NotNull(acknowledged.AcknowledgedAt);

        var edit = app.Telegram.Calls("editMessageText").Last(call => call["chat_id"]!.GetValue<long>() == 1002);
        Assert.Contains("Acknowledged by Alice (@alice)", edit["text"]!.ToString());
        var left = edit["reply_markup"]!["inline_keyboard"]![0]!.AsArray();
        Assert.Equal([$"ign:{alert.Id}"], left.Select(button => button!["callback_data"]!.ToString()));

        var listed = Assert.Single((await owner.GetJsonAsync<AlertPage>("/api/alerts"))!.Items);
        Assert.Equal("tg-ack", listed.AcknowledgedBy);
    }

    [Fact]
    public async Task Ignore_asks_to_confirm_before_it_stops_monitoring()
    {
        var (_, owner, alert) = await FiringAlertAsync("tg-ign@example.com", 1003);
        await app.DispatchAsync();

        await app.HandleAsync(Press(1003, $"ign:{alert.Id}"));
        Assert.Equal(AlertStatus.Firing, (await ReloadAsync(alert.Id)).Status);
        var asked = app.Telegram.Calls("editMessageText").Last(call => call["chat_id"]!.GetValue<long>() == 1003);
        Assert.Contains("Stop monitoring this?", asked["text"]!.ToString());
        var choices = asked["reply_markup"]!["inline_keyboard"]!.AsArray().Select(row => row![0]!["callback_data"]!.ToString());
        Assert.Equal([$"ign!:{alert.Id}", $"back:{alert.Id}"], choices);

        await app.HandleAsync(Press(1003, $"back:{alert.Id}"));
        Assert.Equal(AlertStatus.Firing, (await ReloadAsync(alert.Id)).Status);

        await app.HandleAsync(Press(1003, $"ign!:{alert.Id}"));
        var ignored = await ReloadAsync(alert.Id);
        Assert.Equal(AlertStatus.Resolved, ignored.Status);
        Assert.NotNull(ignored.IgnoredAt);
        Assert.Single((await owner.GetJsonAsync<List<AlertIgnoreResponse>>("/api/alert-ignores"))!);

        var done = app.Telegram.Calls("editMessageText").Last(call => call["chat_id"]!.GetValue<long>() == 1003);
        Assert.Contains("Ignored by Alice (@alice)", done["text"]!.ToString());
        Assert.Empty(done["reply_markup"]!["inline_keyboard"]!.AsArray());

        // Closing an ignored alert sends no "resolved" message.
        var before = app.Telegram.Calls("sendMessage").Count;
        await app.DispatchAsync();
        Assert.Equal(before, app.Telegram.Calls("sendMessage").Count);
    }

    [Fact]
    public async Task Start_replies_with_the_chat_id()
    {
        await SaveBotAsync();

        await app.HandleAsync(new TelegramUpdate(
            UpdateId: 2,
            Message: new TelegramMessage(1, new TelegramChat(31337, "private", null, null), new TelegramUser(5, false, "Bob", null, null), "/start"),
            ChannelPost: null,
            CallbackQuery: null));

        var reply = app.Telegram.Calls("sendMessage").Last();
        Assert.Equal("31337", reply["chat_id"]!.ToString());
        Assert.Contains("<code>31337</code>", reply["text"]!.ToString());

        // In a group the bot only answers its commands.
        var count = app.Telegram.Calls("sendMessage").Count;
        await app.HandleAsync(new TelegramUpdate(
            3, new TelegramMessage(2, new TelegramChat(-500, "group", "Ops", null), null, "hello all"), null, null));
        Assert.Equal(count, app.Telegram.Calls("sendMessage").Count);
        await app.HandleAsync(new TelegramUpdate(
            4, new TelegramMessage(3, new TelegramChat(-500, "group", "Ops", null), null, "/chatid@argus_test_bot"), null, null));
        Assert.Equal("-500", app.Telegram.Calls("sendMessage").Last()["chat_id"]!.ToString());
    }

    [Fact]
    public async Task Sending_without_a_bot_says_so()
    {
        var owner = await app.CreateOwnerAsync("tg-none@example.com");
        await app.WithScopeAsync(async services =>
        {
            await services.GetRequiredService<TelegramSettingsStore>().ClearAsync(Ct);
            return 0;
        });
        var created = await owner.PostAsJsonAsync("/api/notification-channels", new { name = "Phone", kind = "Telegram", target = "1004" }, Ct);
        var channel = (await created.Content.ReadFromJsonAsync<JsonObject>(Ct))!;

        var test = await owner.PostAsync($"/api/notification-channels/{channel["id"]}/test", null, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, test.StatusCode);
        Assert.Contains("Telegram is not set up", await test.Content.ReadAsStringAsync(Ct));
    }
}
