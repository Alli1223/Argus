using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent.Tests.Transport;

public class ArgusClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static (ArgusClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://argus.test/base/") };
        return (new ArgusClient(http), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MetricsBatch Batch(int samples) =>
        new() { Samples = Enumerable.Range(0, samples).Select(i => SampleBufferTests.Sample(i)).ToList() };

    [Fact]
    public async Task Metrics_are_posted_with_the_agent_key_and_the_reply_is_parsed()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK,
            """{"accepted":1,"settings":{"collectionIntervalSeconds":30,"inventoryIntervalMinutes":60,"topProcessCount":5}}"""));

        var result = await client.SendMetricsAsync("argus_ak_key", Batch(1), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Accepted);
        Assert.Equal(30, result.Value.Settings!.CollectionIntervalSeconds);
        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal("https://argus.test/base/api/agent/v1/metrics", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("argus_ak_key", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Large_bodies_are_gzip_compressed()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, """{"accepted":50}"""));

        await client.SendMetricsAsync("argus_ak_key", Batch(50), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Contains("gzip", request.Content!.Headers.ContentEncoding);
        await using var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        var batch = await JsonSerializer.DeserializeAsync(gzip, AgentJsonContext.Default.MetricsBatch, TestContext.Current.CancellationToken);
        Assert.Equal(50, batch!.Samples.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "Rejected")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServerError")]
    [InlineData(HttpStatusCode.BadGateway, "ServerError")]
    public async Task Failures_are_classified(HttpStatusCode status, string expected)
    {
        var (client, _) = Create(_ => Json(status, """{"title":"Nope","detail":"Explained."}"""));

        var result = await client.SendMetricsAsync("argus_ak_key", Batch(1), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure.ToString());
        Assert.Equal((int)status, result.StatusCode);
        Assert.Equal("Explained.", result.Detail);
    }

    [Fact]
    public async Task Rate_limits_carry_the_requested_delay()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)) },
        });

        var result = await client.SendMetricsAsync("argus_ak_key", Batch(1), TestContext.Current.CancellationToken);

        Assert.Equal("RateLimited", result.Failure.ToString());
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Fact]
    public async Task Network_errors_mean_the_server_is_unreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("Connection refused"));

        var result = await client.SendMetricsAsync("argus_ak_key", Batch(1), TestContext.Current.CancellationToken);

        Assert.Equal("Unreachable", result.Failure.ToString());
        Assert.Contains("Connection refused", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_offers_are_read_and_an_absent_offer_is_not_an_error()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, """{"version":"9.9.9","sha256":"abc","size":42}"""));
        var offer = await client.GetUpdateOfferAsync("argus_ak_key", TestContext.Current.CancellationToken);
        Assert.Equal(("9.9.9", "abc", 42L), (offer.Value!.Version, offer.Value.Sha256, offer.Value.Size));

        var (none, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var result = await none.GetUpdateOfferAsync("argus_ak_key", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("https://argus.test/base/api/agent/v1/update/offer", Assert.Single(handler.Requests).Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Update_downloads_are_kept_only_when_they_match_the_offer()
    {
        var build = Encoding.UTF8.GetBytes("new agent");
        var offer = new AgentUpdateOffer { Version = "9.9.9", Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(build)), Size = build.Length };
        var directory = Directory.CreateTempSubdirectory("argus-download-").FullName;
        try
        {
            var path = Path.Combine(directory, "argus-agent.new");
            var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(build) });

            Assert.True((await client.DownloadUpdateAsync("argus_ak_key", offer, path, TestContext.Current.CancellationToken)).IsSuccess);
            Assert.Equal(build, File.ReadAllBytes(path));
            File.Delete(path);

            var tooLong = await client.DownloadUpdateAsync("argus_ak_key", offer with { Size = build.Length - 1 }, path, TestContext.Current.CancellationToken);
            Assert.Equal(FailureKind.Rejected, tooLong.Failure);
            var tampered = await client.DownloadUpdateAsync("argus_ak_key", offer with { Sha256 = new string('0', 64) }, path, TestContext.Current.CancellationToken);
            Assert.Equal("The download does not match the offered SHA-256.", tampered.Detail);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
