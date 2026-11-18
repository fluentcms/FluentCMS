using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Authentication;

// The checks of the sign-in one by one: the return route, the id token, the discovery document and the 401 handler.
public sealed class SignInRulesTests
{
    private const string Issuer = "https://cms.example.com/";
    private const string ClientId = "fluentcms-admin";
    private const string Nonce = "nonce-of-the-sign-in";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly long Expires = Now.AddHours(1).ToUnixTimeSeconds();

    // Under a base with a path, https://admin.example.com/admin/, where a route can resolve to "//host" or "scheme:" inside the
    // base. One pass is enough: the result is what the callback navigates to.
    [Theory]
    [InlineData("content/article/1?locale=fr", "content/article/1?locale=fr")]
    [InlineData(".///evil.example/x", "")]
    [InlineData("x/..///evil.example/x", "")]
    [InlineData("%2e///evil.example/x", "")]
    [InlineData(".//evil.example/x", "")]
    [InlineData("./\\evil.example/x", "")]
    [InlineData("../outside", "")]
    [InlineData("../admin-other/x", "")]
    [InlineData("./javascript:alert(1)", "")]
    [InlineData("x/../javascript:alert(1)", "")]
    [InlineData("./https://evil.example/x", "")]
    [InlineData("%2e/data:x", "")]
    [InlineData("./data:text/html,x", "")]
    [InlineData("./%6aavascript:alert(1)", "")]
    [InlineData("a:", "")]
    [InlineData("content/a:b", "content/a:b")]
    [InlineData("./authentication/login-callback", "")]
    public void TheReturnRouteNeverLeavesTheBaseOfTheApp(string route, string expected)
    {
        var storage = new MemorySessionStorage();
        var configuration = new AdminConfiguration { ApiBaseUrl = new Uri(Issuer) };
        using var authority = new AuthorityClient(configuration, new HttpClient(new FakeAuthority()));
        var session = new AdminSession(configuration, authority, storage, new BrowserTabs().ChannelOf(storage), new FormDrafts(storage), new StubNavigationManager(), TimeProvider.System, NullLogger<AdminSession>.Instance);

        session.AppRoute(route).ShouldBe(expected);
    }

    [Fact]
    public void AnIdTokenOfTheSignInNamesTheUser()
    {
        IdentityToken.Read(Token(Claims()), Issuer, ClientId, Nonce, Now).ShouldBe(new SignedInUser("user-1", "ada", "ada@example.com"));
    }

    [Theory]
    [InlineData("\"aud\":[\"fluentcms-admin\",\"another\"],\"azp\":\"fluentcms-admin\"")]
    [InlineData("\"aud\":[\"fluentcms-admin\"]")]
    [InlineData("\"azp\":\"fluentcms-admin\"")]
    public void SeveralAudiencesAreAcceptedOnlyWithTheClientAsTheAuthorizedParty(string audience)
    {
        IdentityToken.Read(Token(Claims(audience)), Issuer, ClientId, Nonce, Now).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("\"iss\":\"https://evil.example/\"")]
    [InlineData("\"iss\":\"https://cms.example.com\"")]
    [InlineData("\"iss\":null")]
    [InlineData("\"iss\":\"\\ud800\"")]
    [InlineData("\"aud\":\"another\"")]
    [InlineData("\"aud\":[\"fluentcms-admin\",\"another\"]")]
    [InlineData("\"aud\":[]")]
    [InlineData("\"aud\":7")]
    [InlineData("\"aud\":[7]")]
    [InlineData("\"azp\":\"another\"")]
    [InlineData("\"azp\":7")]
    [InlineData("\"nonce\":\"another\"")]
    [InlineData("\"nonce\":null")]
    [InlineData("\"exp\":\"1900000000\"")]
    [InlineData("\"exp\":1.5")]
    [InlineData("\"exp\":1000")]
    [InlineData("\"sub\":7")]
    [InlineData("\"sub\":\"\"")]
    [InlineData("\"sub\":\"\\ud800\"")]
    [InlineData("\"name\":\"\\udfff\"")]
    [InlineData("\"email\":[]")]
    public void AnIdTokenOfAnotherShapeOrSignInIsRefused(string claim)
    {
        IdentityToken.Read(Token(Claims(claim)), Issuer, ClientId, Nonce, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("aud")]
    [InlineData("nonce")]
    [InlineData("exp")]
    [InlineData("sub")]
    public void AnIdTokenWithoutARequiredClaimIsRefused(string claim)
    {
        var claims = Claims().Replace(Member(claim), "", StringComparison.Ordinal).Replace(",,", ",", StringComparison.Ordinal).Replace("{,", "{", StringComparison.Ordinal).Replace(",}", "}", StringComparison.Ordinal);

        IdentityToken.Read(Token(claims), Issuer, ClientId, Nonce, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.!!.c")]
    public void ATokenThatIsNoJwtIsRefused(string? token)
    {
        IdentityToken.Read(token, Issuer, ClientId, Nonce, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public void APayloadThatIsNoObjectIsRefused(string payload)
    {
        IdentityToken.Read(Token(payload), Issuer, ClientId, Nonce, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://cms.example.com/", "https://cms.example.com")]
    [InlineData("https://cms.example.com", "https://cms.example.com/")]
    [InlineData("https://example.com/cms", "https://example.com/cms/")]
    public void TheIssuerIsTheAuthorityWithOrWithoutOneTrailingSlash(string issuer, string authority)
    {
        Should.NotThrow(() => AuthorityClient.Validate(Discovery(issuer), authority));
    }

    [Theory]
    [InlineData("https://CMS.example.com/", "https://cms.example.com")]
    [InlineData("https://cms.example.com:443/", "https://cms.example.com")]
    [InlineData("https://cms.example.com//", "https://cms.example.com")]
    [InlineData("https://cms.example.com/cms", "https://cms.example.com")]
    public void AnotherIssuerIsRefused(string issuer, string authority)
    {
        Should.Throw<AuthorityConfigurationException>(() => AuthorityClient.Validate(Discovery(issuer), authority));
    }

    [Fact]
    public void AnEndpointOnAnotherOriginIsRefused()
    {
        foreach (var document in new[]
        {
            Discovery(Issuer) with { TokenEndpoint = new Uri("https://evil.example/connect/token") },
            Discovery(Issuer) with { AuthorizationEndpoint = new Uri("http://cms.example.com/connect/authorize") },
            Discovery(Issuer) with { RevocationEndpoint = new Uri("https://cms.example.com:8443/connect/revoke") },
            Discovery(Issuer) with { EndSessionEndpoint = new Uri("https://evil.example/connect/logout") },
            Discovery(Issuer) with { TokenEndpoint = null },
        })
        {
            Should.Throw<AuthorityConfigurationException>(() => AuthorityClient.Validate(document, Issuer));
        }
    }

    [Fact]
    public void AServerThatListsMethodsWithoutS256IsRefusedAndOneThatListsNoneIsAccepted()
    {
        Should.Throw<AuthorityConfigurationException>(() => AuthorityClient.Validate(Discovery(Issuer) with { CodeChallengeMethodsSupported = ["plain"] }, Issuer));
        Should.NotThrow(() => AuthorityClient.Validate(Discovery(Issuer) with { CodeChallengeMethodsSupported = null }, Issuer));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, false, 0, 1)]
    [InlineData(HttpStatusCode.Forbidden, "access-1", false, 0, 1)]
    [InlineData(HttpStatusCode.Unauthorized, "access-1", false, 1, 1)]
    [InlineData(HttpStatusCode.Unauthorized, "access-1", true, 1, 2)]
    public async Task OnlyA401ToABearerRequestRefreshesAndOnlyAFreshTokenSendsItAgain(HttpStatusCode status, string? token, bool refreshed, int refreshes, int sends)
    {
        var tokens = new CountingTokens(refreshed);
        var server = new FixedAnswer(status);
        using var invoker = new HttpMessageInvoker(new TokenRefreshHandler(tokens) { InnerHandler = server });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cms.example.com/api/management/v1/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await invoker.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        tokens.Rejected.Count.ShouldBe(refreshes);
        server.Sends.ShouldBe(sends);
    }

    [Fact]
    public void EveryFailureOfTheSignInHasItsText()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        var texts = factory.Create(typeof(AdminResources));

        foreach (var failure in Enum.GetValues<SignInFailure>())
        {
            texts[$"Authentication.Callback.{failure}"].ResourceNotFound.ShouldBeFalse(failure.ToString());
        }

        foreach (var gate in new[] { "Unavailable", "EndedAfterSignIn" })
        {
            texts[$"Authentication.Gate.{gate}Heading"].ResourceNotFound.ShouldBeFalse(gate);
            texts[$"Authentication.Gate.{gate}Text"].ResourceNotFound.ShouldBeFalse(gate);
        }
    }

    [Fact]
    public void ADraftOfANewEntryHasNoBaseVersion()
    {
        var draft = new FormDraft(new FormDraftKey(null, "article", null, "en"), null, []);

        draft.BaseVersion.ShouldBeNull();
        ApiRequest.NewLocale.ShouldBe("\"0\"");
    }

    private static string Member(string claim) => claim switch
    {
        "iss" => $"\"iss\":\"{Issuer}\"",
        "aud" => $"\"aud\":\"{ClientId}\"",
        "nonce" => $"\"nonce\":\"{Nonce}\"",
        "exp" => $"\"exp\":{Expires}",
        "sub" => "\"sub\":\"user-1\"",
        _ => throw new ArgumentOutOfRangeException(nameof(claim)),
    };

    // The claims of the sign-in, the given member put in place of the one of its name.
    private static string Claims(string? replace = null)
    {
        var members = new List<string>
        {
            Member("iss"), Member("aud"), Member("nonce"), Member("exp"), Member("sub"),
            "\"name\":\"ada\"", "\"email\":\"ada@example.com\"",
        };
        if (replace is not null)
        {
            foreach (var part in replace.Split(",\"azp\"", StringSplitOptions.None).Select((part, index) => index == 0 ? part : "\"azp\"" + part))
            {
                var name = part[1..part.IndexOf('"', 1)];
                members.RemoveAll(member => member.StartsWith($"\"{name}\":", StringComparison.Ordinal));
                members.Add(part);
            }
        }

        return "{" + string.Join(',', members) + "}";
    }

    private static string Token(string payload) =>
        $"{Base64Url.EncodeToString("""{"alg":"RS256"}"""u8)}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload))}.c2ln";

    private static OpenIdConfiguration Discovery(string issuer) => new()
    {
        Issuer = issuer,
        AuthorizationEndpoint = new Uri(new Uri(issuer.TrimEnd('/') + "/"), "connect/authorize"),
        TokenEndpoint = new Uri(new Uri(issuer.TrimEnd('/') + "/"), "connect/token"),
        RevocationEndpoint = new Uri(new Uri(issuer.TrimEnd('/') + "/"), "connect/revoke"),
        EndSessionEndpoint = new Uri(new Uri(issuer.TrimEnd('/') + "/"), "connect/logout"),
        CodeChallengeMethodsSupported = ["S256"],
    };

    private sealed class CountingTokens(bool refreshed) : IAccessTokenSource
    {
        public List<string> Rejected { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("access-2");

        public ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken)
        {
            Rejected.Add(rejectedToken);
            return ValueTask.FromResult(refreshed);
        }
    }

    private sealed class FixedAnswer(HttpStatusCode status) : HttpMessageHandler
    {
        public int Sends { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
