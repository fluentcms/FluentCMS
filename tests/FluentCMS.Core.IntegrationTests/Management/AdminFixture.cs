using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Jobs;
using FluentCMS.Jobs;
using FluentCMS.Providers;
using FluentCMS.Settings;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

[Settings("mail")]
public sealed class MailSettings
{
    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 25;

    [SettingsSecret]
    public string? Password { get; set; }
}

public sealed record FailingPayload(string Reason);

// A job that never succeeds, for the dead-letter list.
public sealed class FailingJob : IJob<FailingPayload>
{
    public const string Type = "test.failing";

    public Task ExecuteAsync(FailingPayload payload, JobContext context) => throw new InvalidOperationException(payload.Reason);
}

public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

// The groups of the Management API beside content, on SQLite, with a Super Admin signed in through the authorization server.
// Email is recorded, so that a test can accept an invitation with the token it carried.
public sealed class AdminFixture : IAsyncLifetime
{
    public const string Root = "/api/management/v1/";

    public RecordingEmailSender Email { get; } = new();

    public FluentCmsTestHost Host { get; }

    public HttpClient Admin { get; private set; } = null!;

    public AdminFixture()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes<Page, Person>()
            .WithServices(services =>
            {
                services.AddSingleton<IEmailSender>(Email);

                // Every test signs users in through /connect, which the auth limit would soon refuse.
                services.Configure<RateLimitOptions>(limits =>
                {
                    limits.Auth.PermitLimit = 10_000;
                    limits.Management.PermitLimit = 10_000;
                });
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.PollInterval = TimeSpan.FromDays(1);
                cms.Settings.Add<MailSettings>();
                cms.Jobs.AddJob<FailingJob, FailingPayload>(FailingJob.Type);
            });
    }

    public async ValueTask InitializeAsync() => Admin = await ClientForAsync(BuiltInRoles.SuperAdminName);

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        await Host.DisposeAsync();
    }

    public async Task<HttpClient> ClientForAsync(string role) => await Host.AsUserAsync(await Host.CreateUserAsync(role));

    // An instance-wide role with exactly these grants, and a signed-in user who holds it.
    public async Task<HttpClient> ClientWithAsync(params string[] permissions) => await ClientForAsync(await RoleWithAsync(permissions));

    public async Task<string> RoleWithAsync(params string[] permissions)
    {
        var name = $"role-{Guid.NewGuid():N}";
        await Host.CreateRoleAsync(name, permissions, TestContext.Current.CancellationToken);
        return name;
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

    public Task<ApiResponse> GetAsync(string path, HttpClient? client = null) => SendAsync(client ?? Admin, HttpMethod.Get, path);

    public Task<ApiResponse> PostAsync(string path, string? json = null, HttpClient? client = null) => SendAsync(client ?? Admin, HttpMethod.Post, path, json);

    public Task<ApiResponse> PutAsync(string path, string? json = null, HttpClient? client = null, string? ifMatch = null) => SendAsync(client ?? Admin, HttpMethod.Put, path, json, ifMatch);

    public Task<ApiResponse> PatchAsync(string path, string json, HttpClient? client = null) => SendAsync(client ?? Admin, HttpMethod.Patch, path, json);

    public Task<ApiResponse> DeleteAsync(string path, HttpClient? client = null) => SendAsync(client ?? Admin, HttpMethod.Delete, path);

    public async Task<string> CreateRoleAsync(string json, HttpClient? client = null)
    {
        var created = await PostAsync("roles", json, client);
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        return created.Json["id"]!.GetValue<string>();
    }

    // Rounds of the job runner on the default queue until nothing is due.
    public async Task RunDueJobsAsync()
    {
        while (true)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", TestContext.Current.CancellationToken))
            {
                return;
            }
        }
    }

    public async Task<Guid> EnqueueAsync(FailingPayload payload, DateTimeOffset? runAt = null)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var options = new JobOptions { MaxAttempts = 1 };
        return runAt is { } at
            ? await scheduler.ScheduleAsync<FailingJob, FailingPayload>(payload, at, options, TestContext.Current.CancellationToken)
            : await scheduler.EnqueueAsync<FailingJob, FailingPayload>(payload, options, TestContext.Current.CancellationToken);
    }

    public static string TokenOf(EmailMessage message)
    {
        var link = message.TextBody.Split('\n').Single(line => line.Contains("token=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(new Uri(link.Trim()).Query.Split("token=")[1]);
    }
}
