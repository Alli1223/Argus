using Argus.Contracts.Agent;
using Argus.Server.Infrastructure;

namespace Argus.Server.Tests.Security;

public class SecretTokensTests
{
    [Fact]
    public void Generated_tokens_are_prefixed_unique_and_well_formed()
    {
        var first = SecretTokens.Generate(AgentApi.AgentKeyPrefix);
        var second = SecretTokens.Generate(AgentApi.AgentKeyPrefix);

        Assert.StartsWith(AgentApi.AgentKeyPrefix, first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(SecretTokens.LooksValid(first, AgentApi.AgentKeyPrefix));
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
    }

    [Fact]
    public void Hash_is_stable_lowercase_hex()
    {
        var token = SecretTokens.Generate(AgentApi.EnrollmentTokenPrefix);

        var hash = SecretTokens.Hash(token);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, SecretTokens.Hash(token));
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.NotEqual(hash, SecretTokens.Hash(token + "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("argus_ak_short")]
    [InlineData("argus_et_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Malformed_tokens_are_rejected(string? token)
    {
        Assert.False(SecretTokens.LooksValid(token, AgentApi.AgentKeyPrefix));
    }

    [Fact]
    public void Display_prefix_reveals_only_a_few_characters()
    {
        var token = SecretTokens.Generate(AgentApi.EnrollmentTokenPrefix);

        var display = SecretTokens.DisplayPrefix(token, AgentApi.EnrollmentTokenPrefix);

        Assert.Equal(AgentApi.EnrollmentTokenPrefix.Length + 5, display.Length);
        Assert.EndsWith("…", display, StringComparison.Ordinal);
    }
}
