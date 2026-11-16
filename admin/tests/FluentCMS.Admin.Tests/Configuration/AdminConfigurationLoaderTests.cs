using System.Net;
using FluentCMS.Admin.Configuration;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AdminConfigurationLoaderTests
{
    [Fact]
    public async Task TheFileIsReadFromTheAppBaseWithoutTheBrowserCache()
    {
        var handler = new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }""");

        var startup = await AdminConfigurationLoader.LoadAsync(handler.CreateClient(), TestContext.Current.CancellationToken);

        startup.Configuration.ShouldNotBeNull();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldBe(new Uri("https://admin.example.com/admin/config.json"));

        // The option the browser's HttpClient hands to fetch() as its init object.
        request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var fetchOptions)
            .ShouldBeTrue();
        fetchOptions!["cache"].ShouldBe("no-store");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    public async Task AFileThatCannotBeReadIsAProblemWithItsStatus(HttpStatusCode status, int expected)
    {
        var handler = new FakeHandler("{}", status);

        var startup = await AdminConfigurationLoader.LoadAsync(handler.CreateClient(), TestContext.Current.CancellationToken);

        startup.Configuration.ShouldBeNull();
        var problem = startup.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(ConfigurationProblemKind.Unreadable);
        problem.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task ACharsetThatCannotBeDecodedIsAProblemOfTheFile()
    {
        var handler = new FakeHandler("""{ "apiBaseUrl": "https://cms.example.com" }""") { ContentType = "application/json; charset=bogus" };

        var startup = await AdminConfigurationLoader.LoadAsync(handler.CreateClient(), TestContext.Current.CancellationToken);

        startup.Configuration.ShouldBeNull();
        var problem = startup.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(ConfigurationProblemKind.Unreadable);
        problem.StatusCode.ShouldBeNull();
    }

    [Fact]
    public void TheFetchGivesUpAfterFifteenSeconds()
    {
        AdminConfigurationLoader.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANetworkFailureOrTimeoutIsAProblemWithoutAStatus(bool timeout)
    {
        Exception failure = timeout ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.") : new HttpRequestException("TypeError: Failed to fetch");
        using var http = new HttpClient(new FailingHandler(failure)) { BaseAddress = FakeHandler.AppBase };

        var startup = await AdminConfigurationLoader.LoadAsync(http, TestContext.Current.CancellationToken);

        var problem = startup.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(ConfigurationProblemKind.Unreadable);
        problem.StatusCode.ShouldBeNull();
    }

    private sealed class FailingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw failure;
    }
}
