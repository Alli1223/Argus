using Argus.Server.Features.Updates;

namespace Argus.Server.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("v0.10.0", "0.9.9", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0-beta+abc", false)]
    [InlineData("0.2.0", "0.3.0", false)]
    [InlineData("0.2.0", "not a version", true)]
    [InlineData("latest", "0.1.0", false)]
    public void Newer_releases_are_told_apart(string candidate, string current, bool newer) =>
        Assert.Equal(newer, ReleaseVersions.IsNewer(candidate, current));

    [Fact]
    public void Checksums_are_read_as_sha256sum_writes_them()
    {
        var hash = new string('a', 64);
        var sums = AgentPackages.ParseChecksums($"{hash}  argus-agent-linux-x64\n{hash.ToUpperInvariant()} *argus-agent-win-x64.exe\nnot a line\n");

        Assert.Equal(hash, sums["argus-agent-linux-x64"]);
        Assert.Equal(hash, sums["argus-agent-win-x64.exe"]);
        Assert.Equal(2, sums.Count);
    }
}
