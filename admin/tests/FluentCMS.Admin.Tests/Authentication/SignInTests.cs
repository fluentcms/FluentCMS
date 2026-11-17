using System.Net;
using System.Text.Json;
using System.Web;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Account;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Api;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Authentication;

// The admin's own code flow with PKCE against a fake token endpoint. Each page load of the tab is a context of its own, as
// a reload is in the browser: memory starts empty, and the tab's sessionStorage and the server carry over.
public sealed class SignInTests : IAsyncDisposable
{
    private const string Route = "content/article?locale=fr";
    private const string Callback = "http://localhost/authentication/login-callback";
    private const string Unauthorized = """{"status":401,"code":"FCMS3102","title":"Unauthorized"}""";
    private const string MeJson = """{"id":"01a1e1f8-ff43-7810-88a0-6e0124e0827a","kind":"adminUser","name":"ada"}""";

    private static readonly Guid EntryId = Guid.Parse("01a1e1fa-4db2-7614-9c93-b8a7f6254fa5");

    private readonly MemorySessionStorage _storage = new();
    private readonly FakeAuthority _authority = new();
    private readonly MovableTime _time = new();
    private readonly List<Tab> _tabs = [];
    private DateTimeOffset _signedInAt;

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in _tabs)
        {
            await tab.DisposeAsync();
        }
    }

    [Fact]
    public void ASignedOutTabGoesToTheAuthorizeEndpointWithPkce()
    {
        var tab = NewTab();

        var app = tab.Load(Route);

        var redirect = tab.WaitForRedirect(app);
        redirect.GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/authorize");
        var query = HttpUtility.ParseQueryString(redirect.Query);
        var pending = Pending();
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe("fluentcms-admin");
        query["redirect_uri"].ShouldBe(Callback);
        query["scope"].ShouldBe("openid profile email offline_access management");
        query["code_challenge_method"].ShouldBe("S256");
        query["code_challenge"].ShouldBe(ProofKey.Challenge(pending.Verifier));
        query["state"].ShouldBe(pending.State);
        query["nonce"].ShouldBe(pending.Nonce);
        query.AllKeys.ShouldNotContain("code_verifier");
        redirect.OriginalString.ShouldNotContain(pending.Verifier);
        new[] { pending.State, pending.Nonce, pending.Verifier }.ShouldAllBe(secret => secret.Length == 43);
        new[] { pending.State, pending.Nonce, pending.Verifier }.Distinct().Count().ShouldBe(3);
        pending.ReturnRoute.ShouldBe(Route);
        app.Find("p.sign-in-gate[role=status]").TextContent.ShouldBe("Signing in…");
    }

    [Fact]
    public void TwoSignInsHaveTheirOwnSecrets()
    {
        NewTab().StartSignIn(Route);
        var first = Pending();
        NewTab().StartSignIn(Route);

        var second = Pending();

        (second.State, second.Nonce, second.Verifier).ShouldNotBe((first.State, first.Nonce, first.Verifier));
    }

    [Fact]
    public void TheCallbackRedeemsTheCodeAndReturnsToTheRoute()
    {
        NewTab().StartSignIn(Route);
        var pending = Pending();
        _authority.Nonce = pending.Nonce;
        var tab = NewTab();

        var app = tab.Load(CallbackFor(pending.State));

        app.WaitForAssertion(() => tab.Navigation.Uri.ShouldBe("http://localhost/" + Route));
        var returned = tab.Navigation.History.First();
        returned.Options.ReplaceHistoryEntry.ShouldBeTrue();
        returned.Options.ForceLoad.ShouldBeFalse();
        var redeem = _authority.Requests.Single(request => request.Path == "/connect/token");
        redeem.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = FakeAuthority.Code,
            ["redirect_uri"] = Callback,
            ["client_id"] = "fluentcms-admin",
            ["code_verifier"] = pending.Verifier,
        }, ignoreOrder: true);
        tab.Session.User.ShouldBe(new SignedInUser(_authority.Subject, "ada", "ada@example.com"));
        _storage[AdminSession.SignInKey].ShouldBeNull();
        app.Find("main#main h1").TextContent.ShouldBe("Content type not found");
    }

    [Fact]
    public async Task TheRefreshTokenIsInSessionStorageAndTheAccessTokenInMemoryOnly()
    {
        var tab = SignIn();

        var stored = _storage[AdminSession.SessionKey].ShouldNotBeNull();
        stored.ShouldContain("refresh-1");
        stored.ShouldContain(_authority.Subject);
        _storage.Items.Values.ShouldAllBe(value => !value.Contains("access-1", StringComparison.Ordinal));
        (await tab.Session.GetAccessTokenAsync(Token)).ShouldBe("access-1");
    }

    [Theory]
    [InlineData("forged")]
    [InlineData(null)]
    public void TheCallbackRefusesAStateTheTabDidNotIssue(string? state)
    {
        NewTab().StartSignIn(Route);
        _authority.Nonce = Pending().Nonce;
        var tab = NewTab();

        var app = tab.Load(state is null ? $"authentication/login-callback?code={FakeAuthority.Code}" : CallbackFor(state));

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("This sign-in was not started in this tab, or it has been completed already."));
        app.Find("main#main h1").TextContent.ShouldBe("The sign-in did not complete");
        _authority.Requests.ShouldNotContain(request => request.Path == "/connect/token");
        _storage[AdminSession.SignInKey].ShouldBeNull();
        tab.Session.User.ShouldBeNull();
    }

    [Fact]
    public void TheCallbackRefusesAnErrorAnswer()
    {
        NewTab().StartSignIn(Route);
        var tab = NewTab();

        var app = tab.Load($"authentication/login-callback?error=access_denied&state={Uri.EscapeDataString(Pending().State)}");

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("The server did not sign you in."));
        _authority.Requests.ShouldNotContain(request => request.Path == "/connect/token");
    }

    [Fact]
    public void ACodeIsRedeemedOnceAndItsCallbackIsNotAcceptedAgain()
    {
        NewTab().StartSignIn(Route);
        var pending = Pending();
        _authority.Nonce = pending.Nonce;
        var first = NewTab();
        first.WaitForSignIn(first.Load(CallbackFor(pending.State)));
        var again = NewTab();

        var app = again.Load(CallbackFor(pending.State));

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("This sign-in was not started in this tab, or it has been completed already."));
        _authority.Requests.Count(request => request.Path == "/connect/token").ShouldBe(1);
    }

    [Fact]
    public void ACallbackOfASignInStartedTooLongAgoIsRefused()
    {
        NewTab().StartSignIn(Route);
        var pending = Pending();
        _time.Now += AdminSession.SignInLifetime + TimeSpan.FromSeconds(1);
        var tab = NewTab();

        var app = tab.Load(CallbackFor(pending.State));

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("The sign-in was started too long ago."));
        _authority.Requests.ShouldNotContain(request => request.Path == "/connect/token");
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("audience")]
    public void AnIdTokenOfAnotherSignInOrClientIsRefused(string wrong)
    {
        NewTab().StartSignIn(Route);
        var pending = Pending();
        _authority.Nonce = wrong == "nonce" ? ProofKey.NewSecret() : pending.Nonce;
        _authority.Audience = wrong == "audience" ? "another-client" : FakeAuthority.ClientId;
        var tab = NewTab();

        var app = tab.Load(CallbackFor(pending.State));

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("The answer of the server could not be accepted."));
        tab.Session.User.ShouldBeNull();
        _storage[AdminSession.SessionKey].ShouldBeNull();
    }

    [Theory]
    [InlineData("content/article/1?locale=fr#title", "content/article/1?locale=fr#title")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("https://evil.example/", "")]
    [InlineData("//evil.example/admin", "")]
    [InlineData("/content/article", "")]
    [InlineData("\\\\evil.example\\admin", "")]
    [InlineData("content\\..\\..\\x", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("../outside", "outside")]
    [InlineData("authentication/login-callback?code=1", "")]
    [InlineData("Authentication/Login-Callback", "")]
    public void TheReturnRouteIsAlwaysARouteOfTheApp(string? route, string expected)
    {
        var tab = NewTab();

        tab.Session.AppRoute(route).ShouldBe(expected);
    }

    [Fact]
    public async Task SignInTakesNoPlaceToGoFromTheCaller()
    {
        var tab = NewTab();

        await tab.Session.SignInAsync("https://evil.example/", Token);

        Pending().ReturnRoute.ShouldBe("");
    }

    [Fact]
    public async Task TheAccessTokenIsRefreshedBeforeItExpires()
    {
        var server = new ScriptedServer().Answer(HttpStatusCode.OK, MeJson).Answer(HttpStatusCode.OK, MeJson);
        var tab = SignIn(server);
        var api = tab.Services.GetRequiredService<IManagementApi>();

        _time.Now += TimeSpan.FromSeconds(_authority.ExpiresIn) - AdminSession.RefreshMargin - TimeSpan.FromSeconds(1);
        await api.SendAsync(new ApiRequest(HttpMethod.Get, "me"), Token);
        _time.Now += TimeSpan.FromSeconds(2);
        await api.SendAsync(new ApiRequest(HttpMethod.Get, "me"), Token);

        server.Requests.Select(request => request.Header("Authorization")).ShouldBe(["Bearer access-1", "Bearer access-2"]);
        _authority.Refreshes.ShouldBe(1);
        _authority.Requests.Last().Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "refresh-1",
            ["client_id"] = "fluentcms-admin",
        }, ignoreOrder: true);
        _storage[AdminSession.SessionKey].ShouldNotBeNull().ShouldContain("refresh-2");
    }

    [Fact]
    public async Task A401RefreshesOnceAndSendsTheRequestAgainWithTheNewToken()
    {
        var server = new ScriptedServer()
            .Problem(HttpStatusCode.Unauthorized, """{"status":401,"code":"FCMS3102","title":"Unauthorized"}""")
            .Answer(HttpStatusCode.OK, MeJson);
        var tab = SignIn(server);

        await tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token);

        server.Requests.Select(request => request.Header("Authorization")).ShouldBe(["Bearer access-1", "Bearer access-2"]);
        _authority.Refreshes.ShouldBe(1);
    }

    [Fact]
    public async Task ASecond401IsTheAnswer()
    {
        var unauthorized = """{"status":401,"code":"FCMS3102","title":"Unauthorized"}""";
        var server = new ScriptedServer().Problem(HttpStatusCode.Unauthorized, unauthorized).Problem(HttpStatusCode.Unauthorized, unauthorized);
        var tab = SignIn(server);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        thrown.Problem.Code.ShouldBe("FCMS3102");
        server.Requests.Count.ShouldBe(2);
        _authority.Refreshes.ShouldBe(1);
    }

    [Fact]
    public async Task ManyCallsRefusedAtOnceRefreshOnce()
    {
        var server = new FirstTokenRefused();
        var tab = SignIn(server);
        var api = tab.Services.GetRequiredService<IManagementApi>();
        _authority.HoldRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, 3).Select(_ => Task.Run(() => api.SendAsync(new ApiRequest(HttpMethod.Get, "me"), Token))).ToList();
        await server.WaitForRefusalsAsync(3);
        _authority.HoldRefresh.SetResult();
        await Task.WhenAll(calls);

        _authority.Refreshes.ShouldBe(1);
        server.Tokens.Where(token => token != "Bearer access-1").ShouldBe(["Bearer access-2", "Bearer access-2", "Bearer access-2"]);
    }

    [Fact]
    public async Task ACallRefusedBeforeAFlightThatArrivesAfterItDoesNotRefreshAgain()
    {
        var tab = SignIn();
        (await tab.Session.RefreshAsync("access-1", Token)).ShouldBeTrue();

        (await tab.Session.RefreshAsync("access-1", Token)).ShouldBeTrue();

        _authority.Refreshes.ShouldBe(1);
        (await tab.Session.GetAccessTokenAsync(Token)).ShouldBe("access-2");
    }

    // The test above on threads, as the thread pool runs calls and the browser does not: a refused call can look at its token
    // while the flight runs and reach the gate after it ended. Each round sends calls refused at once, their arrivals spread
    // over the flight by a spacing that changes from round to round, and wants one refresh. Long tokens, compared as copies,
    // make the look at the token last long enough for a flight to end during it.
    [Fact]
    public async Task CallsRefusedAtOnceRefreshOnceHoweverTheirThreadsInterleave()
    {
        const int Rounds = 300;
        const int Calls = 4;
        var tab = SignIn();
        _authority.AccessTokenPadding = 100_000;
        var refreshedTwice = new List<int>();

        for (var round = 0; round < Rounds; round++)
        {
            var refused = new string((await tab.Session.GetAccessTokenAsync(Token)).ShouldNotBeNull());
            var before = _authority.Refreshes;
            using var start = new Barrier(Calls);
            var spacing = round % 64 * 100;
            var calls = Enumerable.Range(0, Calls).Select(call => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(Token);
                    Thread.SpinWait(call * spacing);
                    return tab.Session.RefreshAsync(refused, Token).AsTask();
                },
                Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap());

            (await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(30), Token)).ShouldAllBe(refreshed => refreshed);
            if (_authority.Refreshes - before != 1)
            {
                refreshedTwice.Add(round);
            }
        }

        refreshedTwice.ShouldBeEmpty();
    }

    // What the account page asks after a change that gives the user a new security stamp.
    [Fact]
    public async Task SigningInAgainFromTheAccountPageReplacesTheSessionAndComesBackToIt()
    {
        var tab = SignIn();
        var before = _storage[AdminSession.SessionKey].ShouldNotBeNull();

        await tab.Session.SignInAsync(AccountPage.Route, Token);

        var redirect = tab.Navigation.History.First();
        redirect.Uri.ShouldStartWith("https://cms.example.com/connect/authorize?");
        redirect.Options.ForceLoad.ShouldBeTrue();
        Pending().ReturnRoute.ShouldBe(AccountPage.Route);

        var again = SignInAgain(new FakeManagementServer());

        again.Navigation.Uri.ShouldBe("http://localhost/account");
        again.Session.User.ShouldNotBeNull();
        _storage[AdminSession.SessionKey].ShouldNotBeNull().ShouldNotBe(before);
        _storage[AdminSession.SignInKey].ShouldBeNull();
        var page = again.App.ShouldNotBeNull();
        page.WaitForAssertion(() => page.Find("main h1").TextContent.ShouldBe("Your account"));
    }

    [Fact]
    public async Task ARefusedRefreshKeepsTheUnsavedFormAndGoesToSignInAgain()
    {
        var server = EntryServer(version: 3);
        server.Problem(HttpStatusCode.Unauthorized, """{"status":401,"code":"FCMS3102","title":"Unauthorized"}""");
        var tab = SignIn(server, $"content/article/{EntryId}");
        var form = tab.Render<DraftForm>(parameters => parameters.Add(p => p.Id, EntryId));
        form.WaitForAssertion(() => form.Find("input.title").GetAttribute("value").ShouldBe("Hello"));
        form.Find("input.title").Change("Hello, world");
        _authority.RefreshError = "invalid_grant";
        _time.Now += AdminSession.RepeatWindow;

        await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        var drafts = JsonDocument.Parse(_storage[FormDrafts.StorageKey].ShouldNotBeNull()).RootElement.EnumerateArray().ToList();
        var draft = drafts.ShouldHaveSingleItem();
        draft.GetProperty("user").GetString().ShouldBe(_authority.Subject);
        draft.GetProperty("site").ValueKind.ShouldBe(JsonValueKind.Null);
        draft.GetProperty("type").GetString().ShouldBe("article");
        draft.GetProperty("entry").GetGuid().ShouldBe(EntryId);
        draft.GetProperty("locale").GetString().ShouldBe("en");
        draft.GetProperty("base_version").GetString().ShouldBe("\"3\"");
        draft.GetProperty("fields").GetProperty("title").GetString().ShouldBe("Hello, world");
        _storage[AdminSession.SessionKey].ShouldBeNull();
        tab.Session.User.ShouldBeNull();
        (await tab.Session.GetAccessTokenAsync(Token)).ShouldBeNull();
        Pending().ReturnRoute.ShouldBe($"content/article/{EntryId}");
        var redirect = tab.Navigation.History.First();
        redirect.Uri.ShouldStartWith("https://cms.example.com/connect/authorize?");
        redirect.Options.ForceLoad.ShouldBeTrue();
        _storage.Writes.SkipWhile(write => write != $"set {FormDrafts.StorageKey}").ShouldBe(
            [$"set {FormDrafts.StorageKey}", $"remove {AdminSession.SessionKey}", $"set {AdminSession.SignInKey}"]);
    }

    [Fact]
    public async Task AfterSignInTheDraftIsRestoredAndSavedWithTheVersionItWasBasedOn()
    {
        await EndTheSessionWithADraftAsync();
        var server = EntryServer(version: 4).Answer(HttpStatusCode.OK, Entry(5, "Hello, world"));
        var tab = SignInAgain(server);

        var form = tab.Render<DraftForm>(parameters => parameters.Add(p => p.Id, EntryId));

        form.WaitForAssertion(() => form.Find("input.title").GetAttribute("value").ShouldBe("Hello, world"));
        form.Instance.Restored.ShouldBeTrue();
        await form.Find("button.save").ClickAsync(new());
        form.WaitForAssertion(() => _storage[FormDrafts.StorageKey].ShouldBeNull());
        var save = server.Requests.Last();
        save.Method.ShouldBe(HttpMethod.Put);
        save.Header("If-Match").ShouldBe("\"3\"");
        save.Body.ShouldBe("""{"title":"Hello, world"}""");
    }

    [Fact]
    public async Task ADraftIsNeverRestoredForAnotherUser()
    {
        await EndTheSessionWithADraftAsync();
        _authority.Subject = "0193a1b2-0000-7000-8000-000000000002";
        var tab = SignInAgain(EntryServer(version: 4));

        var form = tab.Render<DraftForm>(parameters => parameters.Add(p => p.Id, EntryId));

        form.WaitForAssertion(() => form.Find("input.title").GetAttribute("value").ShouldBe("Hello"));
        form.Instance.Restored.ShouldBeFalse();
        _storage[FormDrafts.StorageKey].ShouldBeNull();
    }

    [Fact]
    public async Task SignOutRevokesTheRefreshTokenClearsTheTabAndEndsTheServerSession()
    {
        var tab = SignIn();

        await tab.Session.SignOutAsync(Token);

        var revoke = _authority.Requests.Single(request => request.Path == "/connect/revoke");
        revoke.Form.ShouldBe(new Dictionary<string, string>
        {
            ["token"] = "refresh-1",
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = "fluentcms-admin",
        }, ignoreOrder: true);
        AssertSignedOut(tab);
    }

    [Fact]
    public async Task SignOutClearsTheTabWhenTheRevocationFails()
    {
        var tab = SignIn();
        _authority.RevokeStatus = HttpStatusCode.InternalServerError;

        await tab.Session.SignOutAsync(Token);

        AssertSignedOut(tab);
    }

    [Fact]
    public async Task SignOutClearsTheTabBeforeTheServerAnswers()
    {
        var tab = SignIn();
        _authority.HoldRevoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var signingOut = tab.Session.SignOutAsync(Token);

        _storage.Items.ShouldBeEmpty();
        tab.Session.User.ShouldBeNull();
        signingOut.IsCompleted.ShouldBeFalse();
        _authority.HoldRevoke.SetResult();
        await signingOut;
        AssertSignedOut(tab);
    }

    [Fact]
    public async Task SignOutWaitsOnTheServerForAFewSecondsAtMost()
    {
        var tab = SignIn();
        _authority.HoldRevoke = new TaskCompletionSource();
        var signingOut = tab.Session.SignOutAsync(Token);
        _time.Advance(AdminSession.SignOutWait - TimeSpan.FromMilliseconds(1));
        signingOut.IsCompleted.ShouldBeFalse();

        _time.Advance(TimeSpan.FromMilliseconds(1));

        await signingOut.WaitAsync(TimeSpan.FromSeconds(10), Token);
        AssertSignedOut(tab);
    }

    // The tab is cleared at once, so the gate takes its place until the page leaves; it says so, and not that it signs in.
    [Fact]
    public async Task WhileTheServerIsAskedTheGateSaysThatTheTabSignsOut()
    {
        var tab = SignIn();
        _authority.HoldRevoke = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var signingOut = tab.Session.SignOutAsync(Token);

        var app = tab.App!;
        app.WaitForAssertion(() => app.Find("p.sign-in-gate[role=status]").TextContent.ShouldBe("Signing out…"));
        app.FindAll("[role=status]").Where(status => status.TextContent.Contains("Signing", StringComparison.Ordinal)).ShouldHaveSingleItem();
        tab.Navigation.History.ShouldNotContain(entry => entry.Uri.Contains("/connect/authorize", StringComparison.Ordinal));
        _authority.HoldRevoke.SetResult();
        await signingOut;
    }

    [Fact]
    public void AReloadedTabRestoresItsSessionWithoutARedirect()
    {
        SignIn();
        var tab = NewTab();

        var app = tab.Load("");

        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("FluentCMS admin"));
        _authority.Refreshes.ShouldBe(1);
        tab.Session.User.ShouldNotBeNull().Subject.ShouldBe(_authority.Subject);
        tab.Navigation.History.ShouldAllBe(entry => !entry.Options.ForceLoad);
    }

    [Fact]
    public void AReloadedTabWhoseRefreshTokenIsRefusedSignsInAgain()
    {
        SignIn();
        _authority.Expire("refresh-1");
        _time.Now += AdminSession.RepeatWindow;
        var tab = NewTab();

        var app = tab.Load(Route);

        tab.WaitForRedirect(app).GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/authorize");
        _storage[AdminSession.SessionKey].ShouldBeNull();
        Pending().ReturnRoute.ShouldBe(Route);
    }

    [Fact]
    public void ADiscoveryDocumentOfAnotherIssuerStopsTheSignIn()
    {
        _authority.DiscoveredIssuer = "https://evil.example/";
        var tab = NewTab();

        var app = tab.Load(Route);

        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The sign-in is not set up correctly"));
        tab.Navigation.History.ShouldAllBe(entry => !entry.Options.ForceLoad);
        _storage[AdminSession.SignInKey].ShouldBeNull();
    }

    [Fact]
    public void TryAgainStartsTheSignInOnceTheServerAnswers()
    {
        _authority.DiscoveredIssuer = "https://evil.example/";
        var tab = NewTab();
        var app = tab.Load(Route);
        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The sign-in is not set up correctly"));
        _authority.DiscoveredIssuer = FakeAuthority.Issuer;

        app.Find("main#main fluent-button").Click();

        tab.WaitForRedirect(app).GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/authorize");
        Pending().ReturnRoute.ShouldBe(Route);
    }

    [Fact]
    public void ADiscoveryThatTimesOutShowsTryAgain()
    {
        _authority.DiscoveryHangs = true;
        var tab = NewTab(authorityTimeout: TimeSpan.FromMilliseconds(300));

        var app = tab.Load(Route);

        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The sign-in server cannot be reached"), TimeSpan.FromSeconds(10));
        app.Find("main#main fluent-button").TextContent.Trim().ShouldBe("Try again");
        tab.Navigation.History.ShouldAllBe(entry => !entry.Options.ForceLoad);
    }

    [Fact]
    public void SignInAgainFromAFailedCallbackTellsATimeout()
    {
        var tab = NewTab(authorityTimeout: TimeSpan.FromMilliseconds(300));
        var app = tab.Load(CallbackFor("forged"));
        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The sign-in did not complete"));
        _authority.DiscoveryHangs = true;

        app.Find("main#main fluent-button").Click();

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("The server that signs you in could not be reached."), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task WithoutARefreshTokenA401IsTheAnswerAndNothingRedirects()
    {
        _authority.IssuesRefreshTokens = false;
        var server = new ScriptedServer().Problem(HttpStatusCode.Unauthorized, Unauthorized);
        var tab = SignIn(server);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(401);
        _authority.Refreshes.ShouldBe(0);
        tab.Session.User.ShouldNotBeNull();
        tab.Navigation.History.First().Options.ForceLoad.ShouldBeFalse();
        _storage[AdminSession.SessionKey].ShouldBeNull();
    }

    [Fact]
    public async Task ASessionTheServerEndsRightAfterSignInIsNotSignedInAgainOnItsOwn()
    {
        var server = new ScriptedServer().Problem(HttpStatusCode.Unauthorized, Unauthorized);
        var tab = SignIn(server, Route);
        _authority.RefreshError = "invalid_grant";

        await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        var app = tab.App!;
        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The session ended right after sign-in"));
        tab.Navigation.History.First().Options.ForceLoad.ShouldBeFalse();
        _storage[AdminSession.SessionKey].ShouldBeNull();

        app.Find("main#main fluent-button").Click();

        tab.WaitForRedirect(app).GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/authorize");
        Pending().ReturnRoute.ShouldBe(Route);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(null, true)]
    public async Task AServerErrorANetworkFailureOrARefusalWithoutAnOAuthErrorKeepsTheSession(HttpStatusCode? status, bool network)
    {
        var server = new ScriptedServer().Problem(HttpStatusCode.Unauthorized, Unauthorized);
        var tab = SignIn(server);
        _authority.RefreshStatus = status;
        _authority.RefreshFailsOnTheNetwork = network;

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(401);
        _authority.Refreshes.ShouldBe(1);
        tab.Session.User.ShouldNotBeNull();
        (await tab.Session.GetAccessTokenAsync(Token)).ShouldBe("access-1");
        _storage[AdminSession.SessionKey].ShouldNotBeNull().ShouldContain("refresh-1");
        tab.Navigation.History.ShouldAllBe(entry => !entry.Options.ForceLoad);
    }

    [Fact]
    public void AReloadRightAfterSignInWhoseRefreshIsRefusedWaitsForTheUser()
    {
        SignIn();
        _authority.Expire("refresh-1");
        var tab = NewTab();

        var app = tab.Load(Route);

        app.WaitForAssertion(() => app.Find("main#main h1").TextContent.ShouldBe("The session ended right after sign-in"));
        tab.Navigation.History.ShouldAllBe(entry => !entry.Options.ForceLoad);
        _storage[AdminSession.SessionKey].ShouldBeNull();
    }

    [Fact]
    public async Task WithoutARefreshTokenAShortSessionEndsAndSignsInAgainAtItsExpiry()
    {
        _authority.IssuesRefreshTokens = false;
        _authority.ExpiresIn = 60;
        var tab = SignIn(route: Route);

        _time.Now += TimeSpan.FromSeconds(40);
        (await tab.Session.GetAccessTokenAsync(Token)).ShouldBeNull();

        var redirect = tab.Navigation.History.First();
        redirect.Options.ForceLoad.ShouldBeTrue();
        redirect.Uri.ShouldStartWith("https://cms.example.com/connect/authorize?");
        _time.Now.ShouldBeLessThan(_signedInAt + AdminSession.RepeatWindow);
    }

    [Fact]
    public async Task AStoredSignInWithEmptySecretsIsRefused()
    {
        await _storage.SetAsync(AdminSession.SignInKey, $$"""{"state":"","nonce":"","verifier":"","return_route":"","started_at":"{{_time.Now:O}}"}""");
        var tab = NewTab();

        var app = tab.Load("authentication/login-callback?code=" + FakeAuthority.Code + "&state=");

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("This sign-in was not started in this tab, or it has been completed already."));
        _authority.Requests.ShouldNotContain(request => request.Path == "/connect/token");
    }

    [Fact]
    public async Task A403DoesNotRefresh()
    {
        var server = new ScriptedServer().Problem(HttpStatusCode.Forbidden, """{"status":403,"code":"FCMS3103","title":"Forbidden"}""");
        var tab = SignIn(server);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(403);
        server.Requests.ShouldHaveSingleItem();
        _authority.Refreshes.ShouldBe(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"state":null,"nonce":null,"verifier":null}""")]
    [InlineData("not json")]
    public async Task AStoredSignInWithoutItsSecretsIsRefused(string stored)
    {
        await _storage.SetAsync(AdminSession.SignInKey, stored);
        var tab = NewTab();

        var app = tab.Load(CallbackFor("anything"));

        app.WaitForAssertion(() => app.Find("main#main p").TextContent.ShouldBe("This sign-in was not started in this tab, or it has been completed already."));
        _authority.Requests.ShouldNotContain(request => request.Path == "/connect/token");
    }

    [Fact]
    public async Task AStoredSessionWithoutItsTokenIsNoSession()
    {
        await _storage.SetAsync(AdminSession.SessionKey, "{}");
        var tab = NewTab();

        tab.WaitForRedirect(tab.Load(Route)).GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/authorize");
        _authority.Refreshes.ShouldBe(0);
    }

    [Fact]
    public void TheBrowserStorageIsSessionStorageAndHoldsNoAccessToken()
    {
        var start = NewTab(browserStorage: true);
        start.WaitForRedirect(start.Load(Route));
        var signIn = start.StorageWrites().ShouldHaveSingleItem();
        signIn[0].ShouldBe(AdminSession.SignInKey);
        _authority.Nonce = JsonSerializer.Deserialize(signIn[1], AuthenticationJsonContext.Default.PendingSignIn)!.Nonce;
        var tab = NewTab(browserStorage: true, signIn[1]);

        tab.WaitForSignIn(tab.Load(CallbackFor(JsonSerializer.Deserialize(signIn[1], AuthenticationJsonContext.Default.PendingSignIn)!.State)));

        tab.StorageWrites().ShouldContain(write => write[0] == AdminSession.SessionKey && write[1].Contains("refresh-1"));
        foreach (var tabOfTest in new[] { start, tab })
        {
            // localStorage holds the theme only.
            tabOfTest.JSInterop.Invocations.ShouldAllBe(call =>
                !call.Identifier.StartsWith("localStorage", StringComparison.Ordinal) || (string?)call.Arguments[0] == ThemeStorageInterop.Key);
            tabOfTest.JSInterop.Invocations.SelectMany(call => call.Arguments).OfType<string>().ShouldAllBe(argument => !argument.Contains("access-1", StringComparison.Ordinal));
        }
    }

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private static string CallbackFor(string state) => $"authentication/login-callback?code={FakeAuthority.Code}&state={Uri.EscapeDataString(state)}";

    private static string Entry(int version, string title) =>
        $$"""{"id":"{{EntryId}}","status":"draft","locale":"en","version":{{version}},"title":"{{title}}"}""";

    private static ScriptedServer EntryServer(int version) =>
        new ScriptedServer().Answer(HttpStatusCode.OK, Entry(version, "Hello"), configure: response => response.Headers.TryAddWithoutValidation("ETag", $"\"{version}\""));

    private void AssertSignedOut(Tab tab)
    {
        _storage.Items.ShouldBeEmpty();
        tab.Session.User.ShouldBeNull();
        var redirect = tab.Navigation.History.First();
        redirect.Options.ForceLoad.ShouldBeTrue();
        var uri = new Uri(redirect.Uri);
        uri.GetLeftPart(UriPartial.Path).ShouldBe("https://cms.example.com/connect/logout");
        var query = HttpUtility.ParseQueryString(uri.Query);
        query.AllKeys.ShouldBe(["client_id", "post_logout_redirect_uri"], ignoreOrder: true);
        query["client_id"].ShouldBe("fluentcms-admin");
        query["post_logout_redirect_uri"].ShouldBe("http://localhost/");
    }

    private async Task EndTheSessionWithADraftAsync()
    {
        var server = EntryServer(version: 3);
        server.Problem(HttpStatusCode.Unauthorized, """{"status":401,"code":"FCMS3102","title":"Unauthorized"}""");
        var tab = SignIn(server, $"content/article/{EntryId}");
        var form = tab.Render<DraftForm>(parameters => parameters.Add(p => p.Id, EntryId));
        form.WaitForAssertion(() => form.Find("input.title").GetAttribute("value").ShouldBe("Hello"));
        form.Find("input.title").Change("Hello, world");
        _authority.RefreshError = "invalid_grant";
        _time.Now += AdminSession.RepeatWindow;
        await Should.ThrowAsync<ApiProblemException>(() => tab.Services.GetRequiredService<ManagementClient>().GetMeAsync(Token));
        _storage[FormDrafts.StorageKey].ShouldNotBeNull();
        _authority.RefreshError = null;
    }

    private PendingSignIn Pending() =>
        JsonSerializer.Deserialize(_storage[AdminSession.SignInKey].ShouldNotBeNull(), AuthenticationJsonContext.Default.PendingSignIn)!;

    // A tab that signed in from the route, through the authorize redirect and the callback.
    private Tab SignIn(HttpMessageHandler? api = null, string route = "")
    {
        NewTab().StartSignIn(route);
        return SignInAgain(api);
    }

    // The callback of the sign-in the tab started last.
    private Tab SignInAgain(HttpMessageHandler? api = null)
    {
        var pending = Pending();
        _authority.Nonce = pending.Nonce;
        _authority.IssueCode();
        var tab = NewTab(api: api);
        tab.WaitForSignIn(tab.Load(CallbackFor(pending.State)));
        _signedInAt = _time.Now;
        return tab;
    }

    private Tab NewTab(bool browserStorage = false, string? pendingSignIn = null, HttpMessageHandler? api = null, TimeSpan? authorityTimeout = null)
    {
        var tab = new Tab(_storage, _authority, _time, api, browserStorage, pendingSignIn, authorityTimeout);
        _tabs.Add(tab);
        return tab;
    }

    // One page load of the tab.
    private sealed class Tab : AdminTestContext
    {
        public Tab(MemorySessionStorage storage, FakeAuthority authority, TimeProvider time, HttpMessageHandler? api, bool browserStorage, string? pendingSignIn, TimeSpan? authorityTimeout)
        {
            UseNeutralResources();
            UseSignInFlow();
            Storage = storage;
            Authority = authority;
            Start();
            Services.AddSingleton(time);
            if (authorityTimeout is { } timeout)
            {
                Services.Configure<AuthorityOptions>(options => options.Timeout = timeout);
            }
            if (browserStorage)
            {
                Services.AddScoped<FluentCMS.Admin.Interop.ISessionStorage, FluentCMS.Admin.Interop.SessionStorageInterop>();
                JSInterop.Setup<string?>("sessionStorage.getItem", call => (string?)call.Arguments[0] == AdminSession.SignInKey).SetResult(pendingSignIn);
            }

            var scripted = api ?? new ScriptedServer();
            Services.Configure<ManagementApiOptions>(options => options.PrimaryHandler = () =>
                new ShellReadsAside(Management, scripted, () => Services.GetRequiredService<AdminContext>().Status == ShellStatus.Loading));
        }

        public IRenderedComponent<AdminApp>? App { get; private set; }

        public BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

        public AdminSession Session => Services.GetRequiredService<AdminSession>();

        public IRenderedComponent<AdminApp> Load(string route)
        {
            Navigation.NavigateTo("http://localhost/" + route);
            return App = Render<AdminApp>();
        }

        // Starts a sign-in from the route and waits until the page has left for the authorize endpoint.
        public void StartSignIn(string route) => WaitForRedirect(Load(route));

        public Uri WaitForRedirect(IRenderedComponent<AdminApp> app)
        {
            app.WaitForAssertion(() => Navigation.History.First().Options.ForceLoad.ShouldBeTrue());
            return new Uri(Navigation.History.First().Uri);
        }

        // Until the shell has read what it reads after sign-in.
        public void WaitForSignIn(IRenderedComponent<AdminApp> app) => app.WaitForAssertion(() =>
        {
            Session.User.ShouldNotBeNull();
            Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready);
        });

        // The setItem calls on sessionStorage, as key and value.
        public List<string[]> StorageWrites() =>
        [
            .. JSInterop.Invocations
                .Where(call => call.Identifier == "sessionStorage.setItem")
                .Select(call => call.Arguments.Select(argument => (string)argument!).ToArray()),
        ];
    }

    // Answers 401 to the first access token and 200 to any other, from any thread, and records the tokens it saw.
    private sealed class FirstTokenRefused : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string> _tokens = [];
        private readonly TaskCompletionSource _refused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _wanted = int.MaxValue;

        public List<string> Tokens
        {
            get
            {
                lock (_gate)
                {
                    return [.. _tokens];
                }
            }
        }

        public async Task WaitForRefusalsAsync(int count)
        {
            lock (_gate)
            {
                _wanted = count;
                if (_tokens.Count(token => token == "Bearer access-1") >= count)
                {
                    _refused.TrySetResult();
                }
            }

            await _refused.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization?.ToString() ?? "";
            lock (_gate)
            {
                _tokens.Add(token);
                if (_tokens.Count(sent => sent == "Bearer access-1") >= _wanted)
                {
                    _refused.TrySetResult();
                }
            }

            return Task.FromResult(token == "Bearer access-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("""{"status":401,"code":"FCMS3102"}""", System.Text.Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MeJson, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
