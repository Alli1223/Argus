using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Telegram;

/// <summary>Whether the bot is hearing from Telegram, for the Settings page.</summary>
public sealed class TelegramBotStatus
{
    public DateTimeOffset? LastPolledAt { get; private set; }

    public string? LastError { get; private set; }

    public void Polled(DateTimeOffset at)
    {
        LastPolledAt = at;
        LastError = null;
    }

    public void Failed(string error) => LastError = error;

    public void Reset()
    {
        LastPolledAt = null;
        LastError = null;
    }
}

/// <summary>
/// Long-polls Telegram for messages to the bot and presses of its buttons. Polling needs no public address,
/// so the buttons work on a server only reachable at home. One server polls per bot: Telegram refuses a
/// second, and a bot with a webhook set cannot be polled at all.
/// </summary>
internal sealed class TelegramPollingService(
    TelegramSettingsStore settings,
    TelegramApi telegram,
    TelegramBotStatus status,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> options,
    TimeProvider time,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private const int PollSeconds = 25;
    private static readonly TimeSpan IdleWait = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundPolling)
        {
            return;
        }

        long offset = 0;
        var failures = 0;
        string? token = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, settings.Changed);
            try
            {
                var bot = await settings.CurrentAsync(stoppingToken);
                if (bot is null)
                {
                    status.Reset();
                    await Task.Delay(IdleWait, time, linked.Token);
                    continue;
                }

                if (bot.Token != token)
                {
                    token = bot.Token;
                    offset = 0;
                    status.Reset();
                }

                var updates = await telegram.GetUpdatesAsync(bot.Token, offset, PollSeconds, linked.Token);
                status.Polled(time.GetUtcNow());
                failures = 0;
                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    await HandleAsync(bot, update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The bot was changed or removed: start again with the new one.
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                var error = ex is TelegramApiException { ErrorCode: 409 }
                    ? $"{ex.Message} Another server may be using this bot, or it has a webhook set."
                    : ex.Message;
                status.Failed(error);
                logger.LogWarning("Could not get updates from Telegram: {Reason}", error);

                var backoff = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(failures, 6))));
                try
                {
                    await Task.Delay(backoff, time, linked.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task HandleAsync(TelegramBot bot, TelegramUpdate update, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(bot, update, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad update must not stop the rest, nor be fetched again forever.
            logger.LogWarning(ex, "Telegram update {UpdateId} could not be handled", update.UpdateId);
        }
    }
}
