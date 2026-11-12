using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// FluentCms:PublicOrigin (#2816): links in emails and the issuer come from it, whatever Host the request names.
public sealed partial class PublicOriginTests
{
    private const string Origin = "https://cms.example.com";
    private const string Forged = "evil.example";
    private const string Password = "Correct-Horse-Battery-9";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AForgotPasswordRequestWithAForgedHostEmailsALinkToThePublicOrigin()
    {
        var emails = new Emails();
        await using var host = NewHost(emails, cms => cms.PublicOrigin = Origin);
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host, baseAddress: new Uri($"https://{Forged}/"));

        using var sent = await browser.SubmitAsync("/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email });

        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AccountMailJobs.RunAsync(host);
        emails.LinkTo(editor.Email).GetLeftPart(UriPartial.Path).ShouldBe($"{Origin}/account/reset-password");
    }

    [Fact]
    public async Task WithoutAPublicOriginTheResetLinkFollowsTheRequest()
    {
        var emails = new Emails();
        await using var host = NewHost(emails);
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host, baseAddress: new Uri("https://cms.other.example/"));

        using var sent = await browser.SubmitAsync("/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email });

        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AccountMailJobs.RunAsync(host);
        emails.LinkTo(editor.Email).GetLeftPart(UriPartial.Path).ShouldBe("https://cms.other.example/account/reset-password");
    }

    [Fact]
    public async Task UnderAPathBaseTheResetLinkCarriesIt()
    {
        var emails = new Emails();
        await using var host = NewHost(emails, cms =>
        {
            cms.PublicOrigin = Origin;
            cms.ReverseProxy.PathBase = "/cms";
        });
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host, baseAddress: new Uri($"https://{Forged}/"));

        using var sent = await browser.SubmitAsync("/cms/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email });

        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AccountMailJobs.RunAsync(host);
        emails.LinkTo(editor.Email).GetLeftPart(UriPartial.Path).ShouldBe($"{Origin}/cms/account/reset-password");
    }

    [Fact]
    public async Task AnInvitationWithAForgedHostLinksToThePublicOrigin()
    {
        var emails = new Emails();
        await using var host = NewHost(emails, cms => cms.PublicOrigin = Origin);
        var admin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = await host.AsUserAsync(admin);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/management/v1/invitations")
        {
            Content = new StringContent("""{ "email": "ada@example.com" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = Forged;
        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        emails.LinkTo("ada@example.com").GetLeftPart(UriPartial.Path).ShouldBe($"{Origin}/account/accept-invitation");
    }

    [Fact]
    public async Task TheDiscoveryDocumentAndTokensNameThePublicOriginAsTheIssuer()
    {
        await using var host = NewHost(new Emails(), cms => cms.PublicOrigin = Origin);
        using var client = host.CreateSecureClient();

        using var discovery = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        discovery.Headers.Host = Forged;
        using var response = await client.SendAsync(discovery, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = await JsonAsync(response);
        document.GetProperty("issuer").GetString().ShouldBe($"{Origin}/");
        document.GetProperty("authorization_endpoint").GetString().ShouldBe($"{Origin}/connect/authorize");
        document.GetProperty("token_endpoint").GetString().ShouldBe($"{Origin}/connect/token");
        document.GetProperty("jwks_uri").GetString().ShouldBe($"{Origin}/.well-known/jwks");

        var admin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var tokens = await host.SignInAsync(admin);
        Payload(tokens.AccessToken).GetProperty("iss").GetString().ShouldBe($"{Origin}/");

        using var api = host.CreateSecureClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        (await api.GetAsync("/api/management/v1/invitations", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(Origin)]
    [InlineData(Origin + "/cms")]
    [InlineData(Origin + "/cms/")]
    public async Task UnderAPathBaseTheIssuerIsTheOriginWithThePathBase(string origin)
    {
        await using var host = NewHost(new Emails(), cms =>
        {
            cms.PublicOrigin = origin;
            cms.ReverseProxy.PathBase = "/cms";
        });
        using var client = host.CreateSecureClient();

        using var response = await client.GetAsync("/cms/.well-known/openid-configuration", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = await JsonAsync(response);
        document.GetProperty("issuer").GetString().ShouldBe($"{Origin}/cms");
        document.GetProperty("token_endpoint").GetString().ShouldBe($"{Origin}/cms/connect/token");
    }

    [Fact]
    public async Task AnEmptyConfiguredOriginKeepsTheOneSetInCode()
    {
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FluentCms:PublicOrigin"] = "" }).Build()))
            .Configure(cms => cms.PublicOrigin = Origin);
        using var client = host.CreateSecureClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", Token);

        (await JsonAsync(response)).GetProperty("issuer").GetString().ShouldBe($"{Origin}/");
    }

    [Fact]
    public async Task WithoutAPublicOriginTheIssuerFollowsTheRequest()
    {
        await using var host = NewHost(new Emails());
        using var client = host.CreateSecureClient();

        using var discovery = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        discovery.Headers.Host = "cms.other.example";
        using var response = await client.SendAsync(discovery, Token);

        var document = await JsonAsync(response);
        document.GetProperty("issuer").GetString().ShouldBe("https://cms.other.example/");
        document.GetProperty("token_endpoint").GetString().ShouldBe("https://cms.other.example/connect/token");
    }

    private static FluentCmsTestHost NewHost(Emails emails, Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder>? configure = null)
    {
        var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton<IEmailSender>(emails)).Configure(AccountMailJobs.RunByTest);
        return configure is null ? host : host.Configure(configure);
    }

    private static async Task<TestUser> CreateUserAsync(FluentCmsTestHost host)
    {
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        (await users.AddPasswordAsync((await users.FindByIdAsync(editor.Id.ToString()))!, Password)).Succeeded.ShouldBeTrue();
        return editor;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private static JsonElement Payload(string jwt)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private sealed partial class Emails : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public Uri LinkTo(string address) => new(Link().Match(_sent.Last(message => message.To == address).TextBody).Value);

        [GeneratedRegex(@"https?://\S+")]
        private static partial Regex Link();
    }
}
