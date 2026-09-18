using System.Text.Json;
using Argus.Server.Features.Notifications;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Features.Settings;

/// <summary>Where the mail server settings in use come from.</summary>
public enum EmailSettingsSource
{
    /// <summary>No mail server is set anywhere, so Argus sends no email.</summary>
    None,

    /// <summary>The Compose file or environment (<c>Argus:Smtp:*</c>).</summary>
    File,

    /// <summary>Saved in the web app, which is what Argus uses even when the file has settings too.</summary>
    App,
}

/// <summary>The mail server an administrator saved in the web app. The password is kept encrypted.</summary>
internal sealed record SavedEmailSettings(
    string Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    string? ProtectedPassword,
    string From,
    string FromName);

/// <summary>New settings from an administrator. A null <see cref="Password"/> keeps the saved one.</summary>
public sealed record EmailSettingsInput(
    string Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    string? Password,
    string From,
    string FromName);

/// <summary>The mail server Argus sends through, and where those settings came from.</summary>
public sealed record EmailSettingsState(
    SmtpOptions Smtp,
    EmailSettingsSource Source,
    bool HasPassword,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

/// <summary>The mail server to send through, as the senders see it.</summary>
public interface IEmailSettings
{
    /// <summary>The settings to send with: the saved ones, else the Compose file's.</summary>
    ValueTask<SmtpOptions> CurrentAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The mail server Argus sends notifications through: what an administrator saved in the web app, else
/// what the Compose file sets. Saved settings are kept in server_settings and their password encrypted
/// with this server's data protection keys, which live in the database with everything else.
/// </summary>
public sealed class EmailSettingsStore : IEmailSettings
{
    public const string SettingKey = "email";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IOptions<SmtpOptions> _file;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<EmailSettingsStore> _logger;

    private Row? _cached;
    private bool _loaded;

    public EmailSettingsStore(
        NpgsqlDataSource dataSource,
        IOptions<SmtpOptions> file,
        IDataProtectionProvider protection,
        TimeProvider time,
        ILogger<EmailSettingsStore> logger)
    {
        _dataSource = dataSource;
        _file = file;
        _protector = protection.CreateProtector("Argus.Settings.Email");
        _time = time;
        _logger = logger;
    }

    /// <summary>The settings to send with, saved ones first. Returns the file's when nothing is saved.</summary>
    public async ValueTask<SmtpOptions> CurrentAsync(CancellationToken cancellationToken) =>
        (await DescribeAsync(cancellationToken)).Smtp;

    /// <summary>The settings in use, without the password, and where they came from.</summary>
    public async ValueTask<EmailSettingsState> DescribeAsync(CancellationToken cancellationToken)
    {
        var row = await LoadAsync(cancellationToken);
        if (row is null)
        {
            var file = _file.Value;
            var source = file.IsConfigured ? EmailSettingsSource.File : EmailSettingsSource.None;
            return new EmailSettingsState(file, source, !string.IsNullOrEmpty(file.Password), null, null);
        }

        var saved = row.Settings;
        var smtp = new SmtpOptions
        {
            Host = saved.Host,
            Port = saved.Port,
            Security = saved.Security,
            Username = saved.Username,
            Password = Unprotect(saved.ProtectedPassword),
            From = saved.From,
            FromName = saved.FromName,
        };
        return new EmailSettingsState(smtp, EmailSettingsSource.App, !string.IsNullOrEmpty(smtp.Password), row.UpdatedAt, row.UpdatedBy);
    }

    /// <summary>Saves settings for the whole server. Without a password, the saved one stays as it is.</summary>
    public async Task SaveAsync(EmailSettingsInput input, string? updatedBy, CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(cancellationToken);
        var password = input.Password switch
        {
            null => existing?.Settings.ProtectedPassword,
            "" => null,
            var entered => _protector.Protect(entered),
        };

        var settings = new SavedEmailSettings(
            input.Host.Trim(),
            input.Port,
            input.Security,
            Empty(input.Username),
            password,
            input.From.Trim(),
            string.IsNullOrWhiteSpace(input.FromName) ? "Argus" : input.FromName.Trim());

        var now = _time.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO server_settings (key, value, updated_at, updated_by)
            VALUES (@key, @value::jsonb, @updated_at, @updated_by)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by
            """,
            new
            {
                key = SettingKey,
                value = JsonSerializer.Serialize(settings, JsonSerializerOptions.Web),
                updated_at = now,
                updated_by = updatedBy,
            },
            cancellationToken: cancellationToken));

        Cache(new Row(settings, now, updatedBy));
    }

    /// <summary>Forgets the saved settings, so the Compose file's are used again. False when none were saved.</summary>
    public async Task<bool> ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM server_settings WHERE key = @key", new { key = SettingKey }, cancellationToken: cancellationToken));
        Cache(null);
        return deleted > 0;
    }

    private async ValueTask<Row?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return _cached;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var stored = await connection.QuerySingleOrDefaultAsync<StoredRow>(new CommandDefinition(
                "SELECT value, updated_at, updated_by FROM server_settings WHERE key = @key",
                new { key = SettingKey },
                cancellationToken: cancellationToken));

            _cached = Read(stored);
            _loaded = true;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private Row? Read(StoredRow? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SavedEmailSettings>(stored.Value, JsonSerializerOptions.Web);
            return settings is null ? null : new Row(settings, stored.UpdatedAt, stored.UpdatedBy);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("The saved email settings could not be read, so the Compose file's are used: {Reason}", ex.Message);
            return null;
        }
    }

    private void Cache(Row? row)
    {
        _cached = row;
        _loaded = true;
    }

    /// <summary>
    /// Data protection keys are lost when the database is restored without them, or when they expire
    /// unused; the password is then gone rather than wrong, and an administrator enters it again.
    /// </summary>
    private string? Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedPassword);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogWarning("The saved mail server password could not be decrypted, so it is treated as unset: {Reason}", ex.Message);
            return null;
        }
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Row(SavedEmailSettings Settings, DateTimeOffset UpdatedAt, string? UpdatedBy);

    private sealed class StoredRow
    {
        public string Value { get; init; } = "";

        public DateTimeOffset UpdatedAt { get; init; }

        public string? UpdatedBy { get; init; }
    }
}
