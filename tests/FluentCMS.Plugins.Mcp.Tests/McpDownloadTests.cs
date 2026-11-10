using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The guard of an upload by URL below the tool: the connection itself, which no fake transport reaches, and the timeout.
public sealed class McpDownloadTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.7")]
    [InlineData("::1")]
    public async Task AConnectionToAHostThatResolvesToAnAddressOfTheHostsOwnNetworkIsRefusedBeforeItIsMade(string host)
    {
        var refused = await Should.ThrowAsync<McpDownloadException>(async () => await McpDownloads.ConnectAsync(new DnsEndPoint(host, 443), Token));

        refused.Code.ShouldBe(McpErrorCodes.UploadSourceRefused);
        refused.Message.ShouldContain("not a public address");
    }

    [Fact]
    public async Task NothingIsSentUntilTheStreamIsReadAndAFetchOverTheTimeoutFails()
    {
        using var never = new NeverAnswers();
        using var downloads = new McpDownloads(Options.Create(new McpOptions { UploadTimeout = TimeSpan.FromMilliseconds(50) }), [new McpDownloadTransport(never)]);

        await using var stream = downloads.Open(McpDownloads.Checked("https://cdn.example.com/slow.png"), Token);
        never.Asked.ShouldBe(0);
        var failed = await Should.ThrowAsync<McpDownloadException>(async () => await stream.ReadExactlyAsync(new byte[16], Token));

        never.Asked.ShouldBe(1);
        failed.Code.ShouldBe(McpErrorCodes.UploadSourceFailed);
    }

    [Fact]
    public async Task TheBodyIsReadAsItComesAndTheCallersCancellationIsNotAFailureOfTheFetch()
    {
        using var served = new FakeDownloads();
        served.Serve("https://cdn.example.com/a.bin", [1, 2, 3]);
        using var downloads = new McpDownloads(Options.Create(new McpOptions()), [new McpDownloadTransport(served)]);
        using var cancelled = new CancellationTokenSource();

        await using var stream = downloads.Open(McpDownloads.Checked("https://cdn.example.com/a.bin"), Token);
        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, Token);
        await using var abandoned = downloads.Open(McpDownloads.Checked("https://cdn.example.com/a.bin"), cancelled.Token);
        await cancelled.CancelAsync();

        buffer[..read].ShouldBe([1, 2, 3]);
        (await stream.ReadAsync(buffer, Token)).ShouldBe(0);
        await Should.ThrowAsync<OperationCanceledException>(async () => await abandoned.ReadExactlyAsync(buffer, cancelled.Token));
    }

    // The arguments of a read tool as the parameters of a REST list, which core's parser reads.
    [Fact]
    public void AFilterInJsonIsTheBracketParametersOfTheRestGrammar()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "post",
              "filter": {
                "category": { "eq": "news" },
                "rating": { "between": [1, 4], "null": false },
                "or": [ { "title": { "startsWith": "A" } }, { "tags": { "in": ["a,b", "c\\d"] } } ],
                "not": { "author.name": { "contains": "x" } }
              },
              "sort": ["-rating", "title"],
              "fields": ["title"],
              "pageSize": 2,
              "withTotal": false,
              "locale": null
            }
            """);

        var parameters = McpQueryArguments.Parameters(document.RootElement, McpQueryArguments.List);

        parameters.Select(parameter => $"{parameter.Key}={parameter.Value}").ShouldBe(
        [
            "filter[category][eq]=news",
            "filter[rating][between]=1,4",
            "filter[rating][null]=false",
            "filter[or][0][title][startsWith]=A",
            "filter[or][1][tags][in]=a\\,b,c\\\\d",
            "filter[not][author.name][contains]=x",
            "sort=-rating,title",
            "fields=title",
            "pageSize=2",
            "withTotal=false",
        ]);
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        public int Asked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
