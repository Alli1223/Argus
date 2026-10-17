using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Argus.Server.Features.Account;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Users;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Auth;

public sealed class TwoFactorTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Signs in and turns two-factor sign-in on, as the Account page does. Returns the key and recovery codes.</summary>
    private async Task<(string Key, IReadOnlyList<string> RecoveryCodes)> EnableAsync(string email)
    {
        await app.CreateUserAsync(email, Password);
        var client = await app.CreateSignedInClientAsync(email, Password);

        var setup = (await (await client.PostAsync("/api/account/two-factor/setup", null, Ct))
            .Content.ReadFromJsonAsync<TwoFactorSetupResponse>(Ct))!;
        Assert.StartsWith("otpauth://totp/Argus:", setup.AuthenticatorUri);
        var key = setup.SharedKey.Replace(" ", "").ToUpperInvariant();

        var wrong = await client.PostAsJsonAsync("/api/account/two-factor/enable", new { code = "000000" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var enabled = await client.PostAsJsonAsync("/api/account/two-factor/enable", new { code = Totp.Code(key) }, Ct);
        enabled.EnsureSuccessStatusCode();
        var codes = (await enabled.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesResponse>(Ct))!.RecoveryCodes;
        Assert.Equal(TwoFactorEndpoints.RecoveryCodeCount, codes.Count);

        // The session that turned it on stays signed in.
        Assert.True((await client.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.TwoFactorEnabled);
        return (key, codes);
    }

    private async Task<(HttpClient Client, HttpResponseMessage Response)> PasswordStepAsync(string email)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct);
        return (client, response);
    }

    [Fact]
    public async Task Sign_in_asks_for_a_code_once_two_factor_is_on()
    {
        var (key, _) = await EnableAsync("tfa-a@example.com");

        var (client, password) = await PasswordStepAsync("tfa-a@example.com");
        Assert.Equal(HttpStatusCode.Accepted, password.StatusCode);
        Assert.True((await password.Content.ReadFromJsonAsync<TwoFactorRequiredResponse>(Ct))!.TwoFactorRequired);

        // The password alone does not sign in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", Ct)).StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/login/two-factor", new { code = "123456" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Wrong code", (await wrong.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);

        var code = Totp.Code(key);
        var signedIn = await client.PostAsJsonAsync("/api/auth/login/two-factor", new { code = $"{code[..3]} {code[3..]}" }, Ct);
        signedIn.EnsureSuccessStatusCode();
        Assert.Equal("tfa-a@example.com", (await client.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Email);
    }

    [Fact]
    public async Task Recovery_codes_work_once_each()
    {
        var (_, codes) = await EnableAsync("tfa-b@example.com");

        var (first, _) = await PasswordStepAsync("tfa-b@example.com");
        var typed = codes[0].Replace("-", "").ToLowerInvariant();
        (await first.PostAsJsonAsync("/api/auth/login/two-factor", new { code = typed }, Ct)).EnsureSuccessStatusCode();

        var (second, _) = await PasswordStepAsync("tfa-b@example.com");
        var again = await second.PostAsJsonAsync("/api/auth/login/two-factor", new { code = codes[0] }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);

        var status = await first.GetJsonAsync<TwoFactorStatusResponse>("/api/account/two-factor");
        Assert.Equal(TwoFactorEndpoints.RecoveryCodeCount - 1, status!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task The_code_step_needs_the_password_step_first()
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login/two-factor", new { code = "123456" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Sign in again", (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);
    }

    [Fact]
    public async Task Turning_it_off_takes_the_password()
    {
        var (key, _) = await EnableAsync("tfa-c@example.com");
        var (client, _) = await PasswordStepAsync("tfa-c@example.com");
        (await client.PostAsJsonAsync("/api/auth/login/two-factor", new { code = Totp.Code(key) }, Ct)).EnsureSuccessStatusCode();

        var wrong = await client.PostAsJsonAsync("/api/account/two-factor/disable", new { password = "not it at all" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("password", (await wrong.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors.Keys);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/account/two-factor/disable", new { password = Password }, Ct)).StatusCode);
        Assert.False((await client.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, (await PasswordStepAsync("tfa-c@example.com")).Response.StatusCode);
    }

    [Fact]
    public async Task Administrators_can_reset_it_for_someone_who_lost_their_phone()
    {
        await EnableAsync("tfa-d@example.com");
        var admin = await app.CreateOwnerAsync("tfa-admin@example.com", Roles.Admin);
        var listed = (await admin.GetJsonAsync<List<UserSummary>>("/api/users"))!.Single(u => u.Email == "tfa-d@example.com");
        Assert.True(listed.TwoFactorEnabled);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{listed.Id}/reset-two-factor", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PasswordStepAsync("tfa-d@example.com")).Response.StatusCode);
    }

    /// <summary>RFC 6238 codes, as an authenticator app makes them from the shared key.</summary>
    private static class Totp
    {
        public static string Code(string base32Key)
        {
            var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            Span<byte> counter = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, step);
            var hash = HMACSHA1.HashData(Base32(base32Key), counter);
            var offset = hash[^1] & 0x0f;
            var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff) % 1_000_000;
            return value.ToString("D6");
        }

        private static byte[] Base32(string text)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var character in text.TrimEnd('='))
            {
                buffer = (buffer << 5) | alphabet.IndexOf(character);
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
            }

            return [.. bytes];
        }
    }
}
