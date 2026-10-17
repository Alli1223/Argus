using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace Argus.Server.Features.Telegram;

/// <summary>The bot Argus sends through: its token and its @name.</summary>
public sealed record TelegramBot(string Token, string Username, string Name);

/// <summary>What the Settings page shows about the bot. The token itself is never sent back.</summary>
public sealed record TelegramSettingsState(TelegramBot? Bot, DateTimeOffset? UpdatedAt, string? UpdatedBy);

/// <summary>
/// The Telegram bot an administrator saved in the web app, one for the whole server. The token is kept in
/// server_settings, encrypted with this server's data protection keys like the mail server password.
/// </summary>
public sealed class TelegramSettingsStore
{
    public const string SettingKey = "telegram";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly ILogger<TelegramSettingsStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TelegramSettingsState? _cached;
    private CancellationTokenSource _changed = new();

    public TelegramSettingsStore(
        NpgsqlDataSource dataSource, IDataProtectionProvider protection, TimeProvider time, ILogger<TelegramSettingsStore> logger)
    {
        _dataSource = dataSource;
        _protector = protection.CreateProtector("Argus.Settings.Telegram");
        _time = time;
        _logger = logger;
    }

    /// <summary>Cancelled when the bot is saved or removed, so a long wait for updates can start over.</summary>
    public CancellationToken Changed => _changed.Token;

    public async ValueTask<TelegramBot?> CurrentAsync(CancellationToken cancellationToken) =>
        (await DescribeAsync(cancellationToken)).Bot;

    public async ValueTask<TelegramSettingsState> DescribeAsync(CancellationToken cancellationToken)
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } loaded)
            {
                return loaded;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var stored = await connection.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
                "SELECT value, updated_at, updated_by FROM server_settings WHERE key = @key",
                new { key = SettingKey },
                cancellationToken: cancellationToken));

            _cached = Read(stored);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(TelegramBot bot, string? updatedBy, CancellationToken cancellationToken)
    {
        var saved = new SavedTelegramSettings(_protector.Protect(bot.Token), bot.Username, bot.Name);
        var now = _time.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO server_settings (key, value, updated_at, updated_by)
            VALUES (@key, @value::jsonb, @updated_at, @updated_by)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by
            """,
            new { key = SettingKey, value = JsonSerializer.Serialize(saved, JsonSerializerOptions.Web), updated_at = now, updated_by = updatedBy },
            cancellationToken: cancellationToken));

        Replace(new TelegramSettingsState(bot, now, updatedBy));
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM server_settings WHERE key = @key", new { key = SettingKey }, cancellationToken: cancellationToken));
        Replace(new TelegramSettingsState(null, null, null));
    }

    private void Replace(TelegramSettingsState state)
    {
        _cached = state;
        // Not disposed: a poller may be reading its token at this moment. It is small and changes rarely.
        Interlocked.Exchange(ref _changed, new CancellationTokenSource()).Cancel();
    }

    private TelegramSettingsState Read(StoredRow? stored)
    {
        if (stored is null)
        {
            return new TelegramSettingsState(null, null, null);
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedTelegramSettings>(stored.Value, JsonSerializerOptions.Web)
                ?? throw new JsonException("The saved Telegram settings are empty.");
            var token = _protector.Unprotect(saved.ProtectedToken);
            return new TelegramSettingsState(new TelegramBot(token, saved.Username, saved.Name), stored.UpdatedAt, stored.UpdatedBy);
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException)
        {
            // Data protection keys lost with a restore: the token is gone rather than wrong, and is entered again.
            _logger.LogWarning("The saved Telegram bot could not be read, so Telegram is off until it is saved again: {Reason}", ex.Message);
            return new TelegramSettingsState(null, stored.UpdatedAt, stored.UpdatedBy);
        }
    }

    private sealed record SavedTelegramSettings(string ProtectedToken, string Username, string Name);

    private sealed class StoredRow
    {
        public string Value { get; init; } = "";

        public DateTimeOffset UpdatedAt { get; init; }

        public string? UpdatedBy { get; init; }
    }
}
