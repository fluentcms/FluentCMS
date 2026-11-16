using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Account;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The account page (A-09) against the fake Management API: each section, its error paths and its texts.
public sealed class AccountPageTests : AdminTestContext
{
    private const string SharedKey = "JBSWY3DPEHPK3PXP";
    private const string AuthenticatorUri = "otpauth://totp/FluentCMS:ada%40example.com?secret=" + SharedKey + "&issuer=FluentCMS&digits=6";
    private static readonly Guid Admin = new("0193a1b2-0000-7000-8000-0000000000c1");
    private static readonly Guid Phone = new("0193a1b2-0000-7000-8000-0000000000c2");
    private static readonly string[] Codes = ["A1B2C-3D4E5", "F6G7H-8J9K0"];

    public AccountPageTests() => UseNeutralResources();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheSessionsAreListedAndTheOneEndedGoes()
    {
        AddSession(Admin, "FluentCMS admin");
        AddSession(Phone, "Phone app");
        Management.Answer = (method, path) =>
        {
            if (method == HttpMethod.Delete && path == $"me/sessions/{Phone:D}")
            {
                Management.Sessions.RemoveAt(1);
                return NoContent();
            }

            return null;
        };
        var app = Load();
        var sessions = Section(app, "Sessions");
        sessions.TextContent.ShouldContain("It does not sign the browser out of the sign-in server");
        Names(sessions).ShouldBe(["FluentCMS admin", "Phone app"]);

        Button(sessions, "End, the sign-in of Phone app").Click();

        app.WaitForAssertion(() => Names(Section(app, "Sessions")).ShouldBe(["FluentCMS admin"]));
        Section(app, "Sessions").QuerySelector(".account-message")!.TextContent.ShouldBe("The sign-in of Phone app is ended.");
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"me/sessions/{Phone:D}");
        Management.Count("me/sessions").ShouldBe(2);
    }

    [Fact]
    public void EndingTheSessionOfThisTabLeavesTheSignInToTheSession()
    {
        AddSession(Admin, "FluentCMS admin");
        var ended = false;
        Management.Answer = (method, path) => (method.Method, path, ended) switch
        {
            ("DELETE", _, _) => Ended(),
            ("GET", "me/sessions", true) => Problem(HttpStatusCode.Unauthorized, "FCMS3102"),
            _ => null,
        };
        var app = Load();

        Button(Section(app, "Sessions"), "End, the sign-in of FluentCMS admin").Click();

        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The sign-in of this tab has ended. The admin signs in again."));
        Section(app, "Sessions").QuerySelectorAll("fluent-button").ShouldBeEmpty();

        HttpResponseMessage Ended()
        {
            ended = true;
            return NoContent();
        }
    }

    [Fact]
    public void AnEnrolmentShowsTheCodeAndKeyThenTheRecoveryCodesOnceAndTheirStepDropsThem()
    {
        EnrolAnswers();
        var app = Load();
        var twoFactor = Section(app, "Two-factor sign-in");
        twoFactor.TextContent.ShouldContain("Two-factor sign-in is off.");

        Button(twoFactor, "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("svg.qr-code").ShouldNotBeNull());
        twoFactor = Section(app, "Two-factor sign-in");
        var qr = twoFactor.QuerySelector("svg.qr-code")!;
        qr.GetAttribute("role").ShouldBe("img");
        qr.GetAttribute("aria-label").ShouldBe("QR code for the authenticator app. The key beside it holds the same.");
        twoFactor.QuerySelector(".account-key")!.TextContent.ShouldBe("jbsw y3dp ehpk 3pxp");
        twoFactor.QuerySelector("input")!.Input("123456");
        Button(twoFactor, "Turn on").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("pre.account-codes").ShouldNotBeNull());
        twoFactor = Section(app, "Two-factor sign-in");
        twoFactor.QuerySelector("pre.account-codes")!.TextContent.ShouldBe(string.Join('\n', Codes));
        twoFactor.QuerySelector("svg.qr-code").ShouldBeNull();
        app.Markup.ShouldNotContain(SharedKey, Case.Insensitive);
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == "me/two-factor/verify" && call.Body == """{"code":"123456"}""");

        Button(twoFactor, "I have saved them").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("Two-factor sign-in is on. Recovery codes left: 10."));
        Section(app, "Two-factor sign-in").TextContent.ShouldContain("The recovery codes are no longer shown.");
        AssertNoSecretShown(app);
        AssertNoSecretKept();
    }

    [Fact]
    public void TheRecoveryCodesAreNotShownAgainOnASecondVisit()
    {
        EnrolAnswers();
        var app = Load();
        Button(Section(app, "Two-factor sign-in"), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("input").ShouldNotBeNull());
        Section(app, "Two-factor sign-in").QuerySelector("input")!.Input("123456");
        Button(Section(app, "Two-factor sign-in"), "Turn on").Click();
        app.WaitForAssertion(() => app.Markup.ShouldContain(Codes[0]));

        Navigation.NavigateTo("");
        app.WaitForAssertion(() => app.FindAll(".account").ShouldBeEmpty());
        Navigation.NavigateTo(AccountPage.Route);

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("Recovery codes left: 10."));
        AssertNoSecretShown(app);
        AssertNoSecretKept();
    }

    [Fact]
    public void AWrongCodeKeepsTheStepWithTheErrorAndLeavingTheStepDropsTheKey()
    {
        EnrolAnswers(wrongCode: true);
        var app = Load();
        Button(Section(app, "Two-factor sign-in"), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("input").ShouldNotBeNull());

        Section(app, "Two-factor sign-in").QuerySelector("input")!.Input("000000");
        Button(Section(app, "Two-factor sign-in"), "Turn on").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("The code is not right"));
        var input = Section(app, "Two-factor sign-in").QuerySelector("input")!;
        input.GetAttribute("aria-invalid").ShouldBe("true");
        Section(app, "Two-factor sign-in").QuerySelector("svg.qr-code").ShouldNotBeNull();

        Button(Section(app, "Two-factor sign-in"), "Cancel").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("Nothing was changed."));
        AssertNoSecretShown(app);
        Management.Count("me/two-factor/verify").ShouldBe(1);
    }

    [Fact]
    public void AnEmptyCodeIsNotSent()
    {
        EnrolAnswers();
        var app = Load();
        Button(Section(app, "Two-factor sign-in"), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("input").ShouldNotBeNull());

        Button(Section(app, "Two-factor sign-in"), "Turn on").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("Enter a code."));
        Management.Count("me/two-factor/verify").ShouldBe(0);
    }

    [Fact]
    public void TwoFactorIsTurnedOffWithARecoveryCode()
    {
        Management.TwoFactor = Status(enabled: true, left: 3);
        Management.Answer = (method, path) =>
        {
            if (path == "me/two-factor/disable")
            {
                Management.TwoFactor = Status(enabled: false, left: 0);
                return NoContent();
            }

            return null;
        };
        var app = Load();
        Section(app, "Two-factor sign-in").TextContent.ShouldContain("Recovery codes left: 3.");

        Button(Section(app, "Two-factor sign-in"), "Turn off").Click();
        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("input").ShouldNotBeNull());
        Section(app, "Two-factor sign-in").QuerySelector("label")!.TextContent.ShouldContain("Code from the app, or a recovery code");
        Section(app, "Two-factor sign-in").QuerySelector("input")!.Input(Codes[0]);
        Button(Section(app, "Two-factor sign-in"), "Turn off").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").TextContent.ShouldContain("Two-factor sign-in is off. With it on"));
        Section(app, "Two-factor sign-in").QuerySelector(".account-message")!.TextContent.ShouldBe("Two-factor sign-in is off.");
        Management.Calls.ShouldContain(call => call.Path == "me/two-factor/disable" && call.Body == $$"""{"code":"{{Codes[0]}}"}""");
    }

    [Fact]
    public void NewRecoveryCodesTakeACodeAndAreShownOnce()
    {
        Management.TwoFactor = Status(enabled: true, left: 1);
        Management.Answer = (method, path) => path == "me/two-factor/recovery-codes"
            ? Json(new JsonObject { ["recoveryCodes"] = new JsonArray([.. Codes.Select(code => (JsonNode)code)]) })
            : null;
        var app = Load();

        Button(Section(app, "Two-factor sign-in"), "New recovery codes").Click();
        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("input").ShouldNotBeNull());
        Section(app, "Two-factor sign-in").QuerySelector("input")!.Input("123456");
        Button(Section(app, "Two-factor sign-in"), "New recovery codes").Click();

        app.WaitForAssertion(() => Section(app, "Two-factor sign-in").QuerySelector("pre.account-codes")!.TextContent.ShouldBe(string.Join('\n', Codes)));
        Button(Section(app, "Two-factor sign-in"), "I have saved them").Click();
        app.WaitForAssertion(() => AssertNoSecretShown(app));
    }

    [Fact]
    public void ARoleThatRequiresTwoFactorIsSaid()
    {
        Management.TwoFactor = Status(enabled: false, left: 0, required: true, pending: true);

        var app = Load();

        var twoFactor = Section(app, "Two-factor sign-in").TextContent;
        twoFactor.ShouldContain("A role of yours requires it");
        twoFactor.ShouldContain("A setup was started and not finished.");
    }

    [Fact]
    public void AKeyOrAServiceAccountGetsTheRefusalOfTheTwoFactorLoginsAndPreferencesRoutes()
    {
        Management.Answer = (method, path) => path is "me/two-factor" or "me/logins" or "me/preferences" ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;

        var app = Load();

        foreach (var heading in new[] { "Two-factor sign-in", "External logins", "Language and theme" })
        {
            Section(app, heading).TextContent.ShouldContain("Only a user has this.");
        }

        Section(app, "Sessions").TextContent.ShouldContain("You have no session.");
        Section(app, "Language and theme").QuerySelector("select").ShouldNotBeNull();
    }

    [Fact]
    public void ASectionTheServerDoesNotAnswerOffersToTryAgain()
    {
        var failing = true;
        Management.Answer = (method, path) => failing && path == "me/logins" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();
        Section(app, "External logins").TextContent.ShouldContain("This could not be read.");

        failing = false;
        Button(Section(app, "External logins"), "Try again").Click();

        app.WaitForAssertion(() => Section(app, "External logins").TextContent.ShouldContain("No external login is linked to your account."));
    }

    [Fact]
    public void AnExternalLoginIsListedAndRemoved()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Logins.Add(Login("github", null));
        Management.Answer = (method, path) =>
        {
            if (method == HttpMethod.Delete && path == "me/logins/github")
            {
                Management.Logins.RemoveAt(1);
                return NoContent();
            }

            return null;
        };
        var app = Load();
        Names(Section(app, "External logins")).ShouldBe(["Corporate sign-in", "github"]);

        Button(Section(app, "External logins"), "Remove, the login of github").Click();

        app.WaitForAssertion(() => Names(Section(app, "External logins")).ShouldBe(["Corporate sign-in"]));
        Section(app, "External logins").QuerySelector(".account-message")!.TextContent.ShouldBe("The login of github is removed.");
    }

    [Fact]
    public void TheLastWayToSignInStays()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Answer = (method, path) => method == HttpMethod.Delete ? Problem(HttpStatusCode.Conflict, "FCMS3090", "provider") : null;
        var app = Load();

        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").Click();

        app.WaitForAssertion(() => Section(app, "External logins").QuerySelector(".account-message")!.TextContent
            .ShouldBe("This login is your last way to sign in, so it stays. Link another provider or set a password first."));
        Names(Section(app, "External logins")).ShouldBe(["Corporate sign-in"]);
    }

    [Fact]
    public void ThePasswordIsChangedOnTheHostedPageInTheSameTabWhichReturnsHereOnTheSameOrigin()
    {
        var app = Load(configuration: """{ "apiBaseUrl": "http://localhost" }""");

        var link = Section(app, "Password").QuerySelector("a")!;
        link.GetAttribute("href").ShouldBe("http://localhost/account/change-password?ReturnUrl=%2Faccount");
        link.HasAttribute("target").ShouldBeFalse();
    }

    [Fact]
    public void OnAnotherOriginThePasswordLinkIsUnderTheAuthoritysPathBaseWithoutAReturnUrl()
    {
        var app = Load(configuration: """{ "apiBaseUrl": "https://cms.example.com", "authority": "https://cms.example.com/cms" }""");

        Section(app, "Password").QuerySelector("a")!.GetAttribute("href").ShouldBe("https://cms.example.com/cms/account/change-password");
    }

    [Fact]
    public void TheThemeChosenHereIsTheShellsAndIsKeptInTheBrowser()
    {
        var app = Load();

        Section(app, "Language and theme").QuerySelectorAll("select")[0].Change("Dark");

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
        JSInterop.VerifyInvoke("localStorage.setItem").Arguments.ShouldBe([ThemeStorageInterop.Key, "dark"]);
        Services.GetRequiredService<ShellTheme>().Mode.ShouldBe(CmsThemeMode.Dark);
    }

    [Fact]
    public void TheLanguageIsSavedInThePreferencesWithTheOthersKept()
    {
        // An interface culture that none of the offered ones matches, so that the select starts without a choice.
        UseInterfaceCulture(PseudoCulture.RightToLeft);
        Management.Preferences = new JsonObject { ["culture"] = null, ["timeZone"] = "Europe/Berlin", ["dateFormat"] = "de-DE", ["numberFormat"] = null };
        var app = Load();

        Section(app, "Language and theme").QuerySelectorAll("select")[1].Change("en");

        app.WaitForAssertion(() => Management.Preferences["culture"]!.GetValue<string>().ShouldBe("en"));
        Management.Preferences["timeZone"]!.GetValue<string>().ShouldBe("Europe/Berlin");
        Management.Preferences["dateFormat"]!.GetValue<string>().ShouldBe("de-DE");
        Management.Calls.Single(call => call.Method == HttpMethod.Put).Path.ShouldBe("me/preferences");
    }

    [Fact]
    public void TheLanguageOfThePreferencesIsShown()
    {
        Management.Preferences["culture"] = "en-GB";

        var app = Load();

        var language = Section(app, "Language and theme").QuerySelectorAll("select")[1];
        language.QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["English"]);
        language.QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("en");
    }

    [Fact]
    public void TheBreadcrumbNamesThePage()
    {
        var app = Load();

        app.Find("nav[aria-label='Breadcrumb']").TextContent.ShouldContain("Account");
        app.Find("main h1").TextContent.ShouldBe("Your account");
    }

    private static JsonObject Status(bool enabled, int left, bool required = false, bool pending = false) => new()
    {
        ["enabled"] = enabled,
        ["required"] = required,
        ["pending"] = pending,
        ["recoveryCodesLeft"] = left,
    };

    private static JsonObject Login(string provider, string? displayName) => new()
    {
        ["provider"] = provider,
        ["providerKey"] = $"key-of-{provider}",
        ["displayName"] = displayName,
    };

    private static List<string> Names(IElement section) =>
        [.. section.QuerySelectorAll(".account-item-name").Select(name => name.TextContent.Trim())];

    private static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text, the visible text and the hidden part.
    private static IElement Button(IElement section, string name) =>
        section.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    private static void AssertNoSecretShown(IRenderedComponent<AdminApp> app)
    {
        app.Markup.ShouldNotContain(SharedKey, Case.Insensitive);
        app.Markup.ShouldNotContain("jbsw y3dp");
        app.FindAll("svg.qr-code").ShouldBeEmpty();
        foreach (var code in Codes)
        {
            app.Markup.ShouldNotContain(code);
        }
    }

    // Nothing of the enrolment reached the URL, the tab's storage or a call to the browser.
    private void AssertNoSecretKept()
    {
        Navigation.Uri.ShouldNotContain(SharedKey, Case.Insensitive);
        Storage.Items.Values.ShouldAllBe(value => !value.Contains(SharedKey, StringComparison.OrdinalIgnoreCase) && !Codes.Any(code => value.Contains(code, StringComparison.Ordinal)));
        JSInterop.Invocations.SelectMany(invocation => invocation.Arguments).OfType<string>()
            .ShouldAllBe(argument => !argument.Contains(SharedKey, StringComparison.OrdinalIgnoreCase) && !Codes.Contains(argument));
    }

    private void EnrolAnswers(bool wrongCode = false) => Management.Answer = (method, path) =>
    {
        switch (path)
        {
            case "me/two-factor/enrol":
                Management.TwoFactor = Status(enabled: false, left: 0, pending: true);
                return Json(new JsonObject { ["sharedKey"] = SharedKey, ["authenticatorUri"] = AuthenticatorUri });
            case "me/two-factor/verify" when wrongCode:
                return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3063", "code");
            case "me/two-factor/verify":
                Management.TwoFactor = Status(enabled: true, left: 10);
                return Json(new JsonObject { ["recoveryCodes"] = new JsonArray([.. Codes.Select(code => (JsonNode)code)]) });
            default:
                return null;
        }
    };

    private void AddSession(Guid id, string clientName) => Management.Sessions.Add(new JsonObject
    {
        ["id"] = id,
        ["clientId"] = "fluentcms-admin",
        ["clientName"] = clientName,
        ["createdAt"] = "2026-11-16T09:30:00+00:00",
        ["scopes"] = new JsonArray("openid", "management"),
    });

    private IRenderedComponent<AdminApp> Load(string route = AccountPage.Route, string configuration = ValidConfiguration)
    {
        Start(configuration);
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() =>
        {
            app.FindAll("section.cms-card").Count.ShouldBe(5);
            app.FindAll("section.cms-card [role=status]").ShouldBeEmpty();
        });
        return app;
    }
}
