using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Argus.Server.Features.Telegram;

/// <summary>The server's Telegram bot. The token is never sent back.</summary>
public sealed record TelegramSettingsResponse(
    bool Configured,
    string? BotUsername,
    string? BotName,
    DateTimeOffset? LastPolledAt,
    string? LastError,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed record TelegramSettingsRequest([property: Required, MaxLength(200)] string BotToken);

public static partial class TelegramEndpoints
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder routes)
    {
        var telegram = routes.MapGroup("/settings/telegram")
            .WithTags("Settings")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        telegram.MapGet("/", DescribeAsync);
        telegram.MapPut("/", SaveAsync);
        telegram.MapDelete("/", ClearAsync);

        return routes;
    }

    private static async Task<TelegramSettingsResponse> DescribeAsync(
        TelegramSettingsStore store, TelegramBotStatus status, CancellationToken cancellationToken)
    {
        var state = await store.DescribeAsync(cancellationToken);
        return new TelegramSettingsResponse(
            state.Bot is not null, state.Bot?.Username, state.Bot?.Name, status.LastPolledAt, status.LastError, state.UpdatedAt, state.UpdatedBy);
    }

    /// <summary>Checks the token with Telegram before saving it, which also gives the bot's @name.</summary>
    private static async Task<Results<Ok<TelegramSettingsResponse>, ValidationProblem>> SaveAsync(
        TelegramSettingsRequest request,
        ClaimsPrincipal user,
        TelegramSettingsStore store,
        TelegramBotStatus status,
        TelegramApi telegram,
        CancellationToken cancellationToken)
    {
        var token = (request.BotToken ?? "").Trim();
        if (!TokenPattern().IsMatch(token))
        {
            return Invalid("That is not a bot token. BotFather gives one like 123456789:AAH4…, a number, a colon and a long code.");
        }

        TelegramUser me;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            me = await telegram.GetMeAsync(token, timeout.Token);
        }
        catch (TelegramApiException ex)
        {
            return Invalid($"Telegram did not accept this token. {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Invalid("Telegram could not be reached from this server, so the token was not checked or saved.");
        }

        await store.SaveAsync(new TelegramBot(token, me.Username ?? "", me.FirstName), user.Identity?.Name, cancellationToken);
        return TypedResults.Ok(await DescribeAsync(store, status, cancellationToken));
    }

    private static async Task<NoContent> ClearAsync(TelegramSettingsStore store, CancellationToken cancellationToken)
    {
        await store.ClearAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ValidationProblem Invalid(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["botToken"] = [message] });

    [GeneratedRegex(@"^\d{5,15}:[A-Za-z0-9_-]{30,64}$")]
    private static partial Regex TokenPattern();
}
