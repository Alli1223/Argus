using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Telegram;

public sealed record TelegramUser(long Id, bool IsBot, string FirstName, string? LastName, string? Username)
{
    /// <summary>How to name someone in a message: "Alice (@alice)", or just the first name.</summary>
    public string DisplayName => Username is null ? FirstName : $"{FirstName} (@{Username})";
}

public sealed record TelegramChat(long Id, string Type, string? Title, string? Username);

public sealed record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text);

public sealed record TelegramCallbackQuery(string Id, TelegramUser From, TelegramMessage? Message, string? Data);

public sealed record TelegramUpdate(
    long UpdateId, TelegramMessage? Message, TelegramMessage? ChannelPost, TelegramCallbackQuery? CallbackQuery);

public sealed record InlineButton(string Text, string? CallbackData = null);

public sealed record TelegramKeyboard(IReadOnlyList<IReadOnlyList<InlineButton>> InlineKeyboard)
{
    public static readonly TelegramKeyboard None = new([]);
}

/// <summary>Telegram refused a request; the message is Telegram's own explanation.</summary>
public sealed class TelegramApiException(string message, int? errorCode) : Exception(message)
{
    public int? ErrorCode { get; } = errorCode;
}

/// <summary>
/// The few Bot API methods Argus uses. The token is part of each URL, so the client this goes through logs
/// nothing, and errors only ever carry Telegram's description.
/// </summary>
public sealed class TelegramApi(IHttpClientFactory clients, IOptions<TelegramOptions> options)
{
    public const string HttpClientName = "telegram";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<TelegramUser> GetMeAsync(string token, CancellationToken cancellationToken) =>
        CallAsync<TelegramUser>(token, "getMe", new { }, cancellationToken);

    /// <summary>Waits up to <paramref name="timeoutSeconds"/> for updates after <paramref name="offset"/>.</summary>
    public Task<List<TelegramUpdate>> GetUpdatesAsync(string token, long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        CallAsync<List<TelegramUpdate>>(
            token,
            "getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message", "channel_post", "callback_query" } },
            cancellationToken);

    /// <summary>Sends HTML text to a chat ID, or to a public channel's @name.</summary>
    public Task<TelegramMessage> SendMessageAsync(
        string token, string chatId, string html, TelegramKeyboard? keyboard, CancellationToken cancellationToken) =>
        CallAsync<TelegramMessage>(
            token,
            "sendMessage",
            new { chat_id = chatId, text = html, parse_mode = "HTML", link_preview_options = new { is_disabled = true }, reply_markup = keyboard },
            cancellationToken);

    public async Task EditMessageAsync(
        string token, long chatId, long messageId, string html, TelegramKeyboard keyboard, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync<JsonElement>(
                token,
                "editMessageText",
                new
                {
                    chat_id = chatId,
                    message_id = messageId,
                    text = html,
                    parse_mode = "HTML",
                    link_preview_options = new { is_disabled = true },
                    reply_markup = keyboard,
                },
                cancellationToken);
        }
        catch (TelegramApiException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // Pressing a button twice asks for the message it already shows.
        }
    }

    /// <summary>Stops the button's spinner, with a short note at the top of the chat when there is one.</summary>
    public Task AnswerCallbackAsync(string token, string callbackId, string? text, CancellationToken cancellationToken) =>
        CallAsync<bool>(token, "answerCallbackQuery", new { callback_query_id = callbackId, text }, cancellationToken);

    private async Task<T> CallAsync<T>(string token, string method, object body, CancellationToken cancellationToken)
    {
        var url = $"{options.Value.ApiUrl.TrimEnd('/')}/bot{token}/{method}";
        using var response = await clients.CreateClient(HttpClientName)
            .PostAsync(url, JsonContent.Create(body, options: Json), cancellationToken);

        TelegramResult<T>? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<TelegramResult<T>>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (result is not { Ok: true, Result: { } value })
        {
            var description = result?.Description ?? $"Telegram answered {(int)response.StatusCode} {response.ReasonPhrase}.";
            throw new TelegramApiException($"Telegram said: {description}", result?.ErrorCode ?? (int)response.StatusCode);
        }

        return value;
    }

    private sealed record TelegramResult<T>(bool Ok, T? Result, string? Description, int? ErrorCode);
}
