using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCms_Plugin1.Admin.Tests;

// The page with the half's services as the admin builds them: the plugin's Configure into a provider of the half, over the
// admin's services, here a Management API that answers the notes.
public sealed class StarterPageTests : BunitContext
{
    private readonly FakeManagementApi _api = new();

    public StarterPageTests()
    {
        Services.AddLocalization();
        Services.AddSingleton<IAdminPluginServices>(new HalfServices(_api));
    }

    [Fact]
    public void ThePageListsTheNotesOfTheSite()
    {
        _api.Notes = """[{ "id": "0193a1c4-6b2e-7c3a-9f1d-2a4b6c8d0e1f", "text": "Hello", "createdAt": "2026-11-17T10:00:00Z" }]""";

        var page = Render<StarterPage>();

        page.WaitForAssertion(() => Assert.Equal("Hello", page.Find("li").TextContent));
        Assert.Equal("starter-plugin/notes", Assert.Single(_api.Paths));
    }

    [Fact]
    public void APageWithoutNotesSaysSo()
    {
        var page = Render<StarterPage>();

        page.WaitForAssertion(() => Assert.Contains("no notes yet", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("li"));
    }

    [Fact]
    public void ARefusedReadIsShownOnThePage()
    {
        _api.Refusal = new ApiProblem { Status = 403, Title = "Forbidden" };

        var page = Render<StarterPage>();

        page.WaitForAssertion(() => Assert.Equal("Forbidden", page.Find("[role=alert]").TextContent));
    }

    private sealed class HalfServices(IManagementApi api) : IAdminPluginServices
    {
        private readonly IServiceProvider _provider = Provider(api);

        public IServiceProvider ProviderOf(Type type) => _provider;

        public ValueTask<TService?> FindAsync<TService>(CancellationToken cancellationToken = default)
            where TService : class => ValueTask.FromResult<TService?>(null);

        private static ServiceProvider Provider(IManagementApi api)
        {
            var services = new ServiceCollection().AddSingleton(api);
            new StarterAdminPlugin().Configure(new Builder(services));
            return services.BuildServiceProvider();
        }
    }

    private sealed class Builder(IServiceCollection services) : IAdminPluginBuilder
    {
        public string Name => StarterAdminPlugin.Name;

        public IServiceCollection Services => services;

        public FluentCMS.Admin.Lists.ListFilterRegistry Filters { get; } = new();
    }

    private sealed class FakeManagementApi : IManagementApi
    {
        public string Notes { get; set; } = "[]";

        public ApiProblem? Refusal { get; set; }

        public List<string> Paths { get; } = [];

        public Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
        {
            Paths.Add(request.Path);
            if (Refusal is not null)
            {
                throw new ApiProblemException(Refusal);
            }

            return Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, null, JsonSerializer.Deserialize(Notes, answer)!));
        }

        public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ApiResponse(HttpStatusCode.NoContent, null));
    }
}
