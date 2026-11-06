using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

public sealed record ApiResponse(HttpStatusCode Status, JsonNode? Body, HttpResponseHeaders Headers)
{
    public JsonNode Json => Body ?? throw new InvalidOperationException($"The response ({Status}) has no body.");

    public string? ETag => Headers.ETag?.ToString();
}

// The default site with English and French, and an editor signed in through the authorization server, as the admin is.
// The job runner does not poll on its own, so a test runs what is due when it moves the clock.
public sealed class ManagementFixture : IAsyncLifetime
{
    public const string Root = "/api/management/v1/content/";

    public FluentCmsTestHost Host { get; } = new FluentCmsTestHost()
        .WithContentTypes<Page, Person>()
        .Configure(cms => cms.Jobs.Options.PollInterval = TimeSpan.FromDays(1));

    public HttpClient Editor { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(DefaultSite.Id, "fr", TestContext.Current.CancellationToken);
        }

        Editor = await ClientForAsync("Editor");
    }

    public async ValueTask DisposeAsync()
    {
        Editor.Dispose();
        await Host.DisposeAsync();
    }

    public async Task<HttpClient> ClientForAsync(string role) => await Host.AsUserAsync(await Host.CreateUserAsync(role));

    // A role of the default site's instance with exactly these grants, and a signed-in user who holds it.
    public async Task<HttpClient> ClientWithAsync(params string[] permissions)
    {
        var role = $"role-{Guid.NewGuid():N}";
        await Host.CreateRoleAsync(role, permissions, TestContext.Current.CancellationToken);
        return await ClientForAsync(role);
    }

    public static async Task<ApiResponse> SendAsync(HttpClient client, HttpMethod method, string path, string? json = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(Root + path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new ApiResponse(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }

    public Task<ApiResponse> GetAsync(string path) => SendAsync(Editor, HttpMethod.Get, path);

    public Task<ApiResponse> PostAsync(string path, string? json = null, string? ifMatch = null) => SendAsync(Editor, HttpMethod.Post, path, json, ifMatch);

    public Task<ApiResponse> PatchAsync(string path, string json, string? ifMatch) => SendAsync(Editor, HttpMethod.Patch, path, json, ifMatch);

    public Task<ApiResponse> DeleteAsync(string path) => SendAsync(Editor, HttpMethod.Delete, path);

    public async Task<string> CreatePageAsync(string title, string? section = null)
    {
        var json = new JsonObject { ["title"] = title, ["section"] = section }.ToJsonString();
        var created = await PostAsync("page", json);
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        return created.Json["id"]!.GetValue<string>();
    }

    // Rounds of the job runner on the default queue until nothing is due; the number of jobs that ran.
    public async Task<int> RunDueJobsAsync()
    {
        var ran = 0;
        while (true)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", TestContext.Current.CancellationToken))
            {
                return ran;
            }

            ran++;
        }
    }
}
