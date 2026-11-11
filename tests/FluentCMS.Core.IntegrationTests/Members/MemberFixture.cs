using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// The host the member tests share: registration and the direct login are on, the public origin is set, mail is captured
// and the limits are out of the way. No worker drains the outbox, so a test drains it before it reads the activity rows.
// A test tells its rows apart from the others' by its own members and addresses.
public sealed class MemberFixture : IAsyncLifetime
{
    public const string Password = "Correct-Horse-Battery-9";
    public const string NewPassword = "A-New-Longer-Passphrase-7";
    public const string Api = "/api/members/v1/";
    public const string Management = "/api/management/v1/";
    public const string TierRule = "perk-tier";
    public const string DirectClientId = "members-app";
    public const string InsiderRole = "Insider";
    public const string GoldRole = "Gold tier";

    public MemberFixture()
    {
        Host = NewHost(Emails, Logs);
    }

    public FluentCmsTestHost Host { get; }

    internal HostedPagesTests.CapturedEmails Emails { get; } = new();

    internal LogCapture Logs { get; } = new();

    public HttpClient Admin { get; private set; } = null!;

    public Guid Insiders { get; private set; }

    public Guid Gold { get; private set; }

    public Guid InsiderRoleId { get; private set; }

    internal static FluentCmsTestHost NewHost(IEmailSender emails, ILoggerProvider? logs = null, Action<DependencyInjection.FluentCmsBuilder>? configure = null) => new FluentCmsTestHost()
        .WithContentTypes<Bulletin, Perk, Dossier>()
        .WithServices(services =>
        {
            services.AddSingleton(emails);
            if (logs is not null)
            {
                services.AddSingleton(logs);
            }

            services.PostConfigure<RateLimitOptions>(limits =>
            {
                limits.Auth.PermitLimit = 10_000;
                limits.Management.PermitLimit = 10_000;
            });
        })
        .Configure(cms =>
        {
            cms.PublicOrigin = "https://localhost";
            cms.Members.AllowRegistration = true;
            cms.Members.AllowDirectLogin = true;
            cms.Jobs.Options.PollInterval = TimeSpan.FromDays(1);
            cms.Jobs.Options.Queues[Jobs.JobRunnerOptions.DefaultQueue] = 0;
            cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            cms.Clients.Applications[DirectClientId] = new ClientRegistration
            {
                Pool = ClientPool.Members,
                AllowDirectLogin = true,
                Scopes = { "openid", "profile", "email", "offline_access" },
            };
            cms.Authorization.AddRowRule<Perk>(TierRule, ctx => perk => perk.Tier == (ctx.Principal.IsInGroup("gold") ? "gold" : "standard"));
            configure?.Invoke(cms);
        })
        .Seed(async services =>
        {
            await PublishAsync(services, new Bulletin { Title = "Open to all" });
            await PublishAsync(services, new Perk { Title = "Standard perk", Tier = "standard" });
            await PublishAsync(services, new Perk { Title = "Gold perk", Tier = "gold" });
            await PublishAsync(services, new Dossier { Title = "For insiders" });
        });

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().AddRange(
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.bulletin.read" },
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = BuiltInRoles.Member, Permission = "content.perk.read", Constraints = new PermissionConstraints { RowRule = TierRule }.ToJson() });
            await context.SaveChangesAsync(token);
        }

        InsiderRoleId = await Host.CreateRoleAsync(InsiderRole, ["content.dossier.read"], token);
        await Host.CreateRoleAsync(GoldRole, [], token);
        Insiders = await Host.CreateMemberGroupAsync("insiders", InsiderRole, token);
        Gold = await Host.CreateMemberGroupAsync("gold", GoldRole, token);
        Admin = await Host.AsUserAsync(await Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: token));
    }

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        await Host.DisposeAsync();
    }

    public static string NewAddress() => $"{Guid.NewGuid():N}@example.com";

    // An instance-wide role with exactly these grants, and a signed-in admin user who holds it.
    public async Task<HttpClient> AdminWithAsync(params string[] permissions)
    {
        var name = $"role-{Guid.NewGuid():N}";
        await Host.CreateRoleAsync(name, permissions, TestContext.Current.CancellationToken);
        return await Host.AsUserAsync(await Host.CreateUserAsync(name, cancellationToken: TestContext.Current.CancellationToken));
    }

    public static async Task<ApiResponse> SendAsync(HttpClient client, HttpMethod method, string url, string? json = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new ApiResponse(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }

    // A call of /api/members/v1 with no credentials, or with the client's.
    public async Task<ApiResponse> MembersAsync(HttpMethod method, string path, string? json = null, HttpClient? client = null)
    {
        using var anonymous = client is null ? Host.CreateSecureClient() : null;
        return await SendAsync(client ?? anonymous!, method, Api + path, json);
    }

    public Task<ApiResponse> ManageAsync(HttpMethod method, string path, string? json = null, HttpClient? client = null) =>
        SendAsync(client ?? Admin, method, Management + path, json);

    public HttpClient Bearer(string accessToken)
    {
        var client = Host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // The titles a caller reads of a type on the Delivery API, or the status when the read is refused.
    public static async Task<(HttpStatusCode Status, string[] Titles)> TitlesAsync(HttpClient client, string type)
    {
        var reply = await SendAsync(client, HttpMethod.Get, $"/api/delivery/v1/{type}?sort=title");
        return (reply.Status, reply.Status == HttpStatusCode.OK ? [.. reply.Json["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())] : []);
    }

    // The token endpoint, answered with its status and JSON whatever it says.
    public async Task<ApiResponse> TokenAsync(IReadOnlyDictionary<string, string> form, string? address = null)
    {
        using var browser = new HostedPagesBrowser(Host, address ?? "10.9.0.1");
        using var response = await browser.PostAsync("/connect/token", form);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new ApiResponse(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }

    public Task<ApiResponse> DirectLoginAsync(string email, string password, string clientId = DirectClientId, string scope = "openid offline_access", string? address = null) =>
        TokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "member_password",
                ["client_id"] = clientId,
                ["username"] = email,
                ["password"] = password,
                ["scope"] = scope,
            },
            address);

    public Task<ApiResponse> RefreshAsync(string refreshToken, string clientId) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
        });

    // The token of the last mail to the address: the token parameter of its link.
    public string TokenOf(string address) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(Emails.LinkTo(address).Query)["token"].ToString();

    public int MailsTo(string address)
    {
        lock (Emails.Sent)
        {
            return Emails.Sent.Count(mail => mail.To == address);
        }
    }

    // A member that registered through the API and verified its address with the link of the mail.
    public async Task<string> RegisterVerifiedAsync(string? email = null, string? displayName = null)
    {
        var address = email ?? NewAddress();
        var name = displayName is null ? "" : $", \"displayName\": \"{displayName}\"";
        (await MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{address}}", "password": "{{Password}}"{{name}} }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await MembersAsync(HttpMethod.Post, "verify-email", $$"""{ "email": "{{address}}", "token": "{{TokenOf(address)}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        return address;
    }

    public async Task<Guid> MemberIdAsync(string address)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var normalized = address.ToUpperInvariant();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsMember>().AsNoTracking()
            .Where(member => member.NormalizedEmail == normalized)
            .Select(member => member.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    // The activity rows once the outbox is delivered.
    public async Task<List<ActivityRow>> ActivityAsync(string actionPrefix, string? targetId = null)
    {
        while (await Host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(TestContext.Current.CancellationToken) > 0)
        {
        }

        await using var scope = Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
            .Where(row => row.Action.StartsWith(actionPrefix) && (targetId == null || row.TargetId == targetId))
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task PublishAsync<T>(IServiceProvider services, T entry)
        where T : FluentCMS.Schema.ContentItem
    {
        var content = services.GetRequiredService<IContentService<T>>();
        var created = await content.CreateAsync(entry);
        await content.PublishAsync(created.Id);
    }
}
