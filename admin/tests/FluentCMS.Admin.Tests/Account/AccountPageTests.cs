using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Account;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The sessions, external logins, password link, theme and language of the account page.
public sealed class AccountPageTests : AccountTestContext
{
    private static readonly Guid Admin = new("0193a1b2-0000-7000-8000-0000000000c1");
    private static readonly Guid Phone = new("0193a1b2-0000-7000-8000-0000000000c2");
    private const string Morning = "2026-11-16T09:30:00+00:00";
    private const string Evening = "2026-11-15T18:05:00+00:00";

    // As the page writes a sign-in's time, in the culture and time zone of the test.
    private string When(string moment) => Services.GetRequiredService<FormatService>().DateTime(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void TheSessionsAreListedAndTheOneEndedGoesWithFocusOnTheResult()
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

        Button(sessions, $"End, the sign-in of Phone app at {When(Morning)}").Click();

        app.WaitForAssertion(() => Names(Section(app, "Sessions")).ShouldBe(["FluentCMS admin"]));
        Message(app, "Sessions").ShouldBe("The sign-in of Phone app is ended.");
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"me/sessions/{Phone:D}");
        Management.Count("me/sessions").ShouldBe(2);
        app.WaitForAssertion(() => ShouldBeFocused<AccountSessions>(app, "messageElement"));
    }

    [Fact]
    public void TwoSignInsOfOneAppAreToldApartByTheirTime()
    {
        AddSession(Admin, "FluentCMS admin", Morning);
        AddSession(Phone, "FluentCMS admin", Evening);

        var app = Load();

        Section(app, "Sessions").QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(
            [$"End, the sign-in of FluentCMS admin at {When(Morning)}", $"End, the sign-in of FluentCMS admin at {When(Evening)}"]);
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

        Button(Section(app, "Sessions"), $"End, the sign-in of FluentCMS admin at {When(Morning)}").Click();

        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The sign-in of this tab has ended. The admin signs in again."));
        Section(app, "Sessions").QuerySelectorAll("fluent-button").ShouldBeEmpty();

        HttpResponseMessage Ended()
        {
            ended = true;
            return NoContent();
        }
    }

    [Fact]
    public void ASessionEndedMeanwhileIsSaidAndTheListReadAgain()
    {
        AddSession(Admin, "FluentCMS admin");
        Management.Answer = (method, path) => method == HttpMethod.Delete ? Problem(HttpStatusCode.NotFound, "FCMS3047") : null;
        var app = Load();

        Button(Section(app, "Sessions"), $"End, the sign-in of FluentCMS admin at {When(Morning)}").Click();

        app.WaitForAssertion(() => Message(app, "Sessions").ShouldBe("That session had ended already."));
        Management.Count("me/sessions").ShouldBe(2);
    }

    [Fact]
    public void AnEndTheServerDoesNotAnswerKeepsTheRowAndTheButtons()
    {
        AddSession(Admin, "FluentCMS admin");
        Management.Answer = (method, path) => method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();

        Button(Section(app, "Sessions"), $"End, the sign-in of FluentCMS admin at {When(Morning)}").Click();

        app.WaitForAssertion(() => Message(app, "Sessions").ShouldBe("The session could not be ended. Try again."));
        Names(Section(app, "Sessions")).ShouldBe(["FluentCMS admin"]);
        Section(app, "Sessions").QuerySelector("fluent-button")!.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void AKeyOrAServiceAccountGetsTheRefusalOfTheTwoFactorAndLoginsRoutes()
    {
        Management.Answer = (method, path) => path is "me/two-factor" or "me/logins" ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;

        var app = Load();

        Section(app, "Two-factor sign-in").TextContent.ShouldContain("Only a user has this.");
        Section(app, "External logins").TextContent.ShouldContain("Only a user has this.");
        Section(app, "Sessions").TextContent.ShouldContain("You have no session.");
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
    public void AnExternalLoginIsListedAndRemovedAndTheTabSignsInAgain()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Logins.Add(Login("github", null));
        Management.Answer = (method, path) => method == HttpMethod.Delete && path == "me/logins/github" ? NoContent() : null;
        var app = Load();
        Names(Section(app, "External logins")).ShouldBe(["Corporate sign-in", "github"]);
        Section(app, "External logins").TextContent.ShouldContain("Removing one ends your sessions, and the admin signs in again at once.");

        Button(Section(app, "External logins"), "Remove, the login of github").Click();

        app.WaitForAssertion(() => SignIns.ShouldBe([AccountPage.Route]));
        Message(app, "External logins").ShouldBe("The login of github is removed.");
    }

    [Fact]
    public void TheLastWayToSignInStays()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Answer = (method, path) => method == HttpMethod.Delete ? Problem(HttpStatusCode.Conflict, "FCMS3090", "provider") : null;
        var app = Load();

        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").Click();

        app.WaitForAssertion(() => Message(app, "External logins")
            .ShouldBe("This login is your last way to sign in, so it stays. Link another provider or set a password first."));
        Names(Section(app, "External logins")).ShouldBe(["Corporate sign-in"]);
        SignIns.ShouldBeEmpty();
    }

    [Fact]
    public void ALoginRemovedMeanwhileIsSaidAndTheListReadAgain()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Answer = (method, path) =>
        {
            if (method == HttpMethod.Delete)
            {
                Management.Logins.Clear();
                return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3091", "provider");
            }

            return null;
        };
        var app = Load();

        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("That login was removed already."));
        Section(app, "External logins").TextContent.ShouldContain("No external login is linked to your account.");
        SignIns.ShouldBeEmpty();
    }

    [Fact]
    public void ARemovalTheServerDoesNotAnswerIsSaid()
    {
        Management.Logins.Add(Login("corp", "Corporate sign-in"));
        Management.Answer = (method, path) => method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();

        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("The login could not be removed. Try again."));
        Section(app, "External logins").QuerySelector("fluent-button")!.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void ThePasswordIsChangedOnTheHostedPageInTheSameTabWhichReturnsHereOnTheSameOrigin()
    {
        var app = Load(configuration: """{ "apiBaseUrl": "http://localhost" }""");

        var password = Section(app, "Password");
        var link = password.QuerySelector("a")!;
        link.GetAttribute("href").ShouldBe("http://localhost/account/change-password?ReturnUrl=%2Faccount");
        link.HasAttribute("target").ShouldBeFalse();
        password.TextContent.ShouldContain("brings you back here");
    }

    [Fact]
    public void OnAnotherOriginThePasswordLinkIsUnderTheAuthoritysPathBaseAndSaysItDoesNotComeBack()
    {
        var app = Load(configuration: """{ "apiBaseUrl": "https://cms.example.com", "authority": "https://cms.example.com/cms" }""");

        var password = Section(app, "Password");
        password.QuerySelector("a")!.GetAttribute("href").ShouldBe("https://cms.example.com/cms/account/change-password");
        password.TextContent.ShouldContain("It does not bring you back here");
        password.TextContent.ShouldNotContain("brings you back here");
    }

    [Fact]
    public void TheThemeChosenHereIsTheShellsAndIsKeptInTheBrowser()
    {
        var app = Load();

        Section(app, "Theme").QuerySelector("select")!.Change("Dark");

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
        JSInterop.VerifyInvoke("localStorage.setItem").Arguments.ShouldBe([ThemeStorageInterop.Key, "dark"]);
        Services.GetRequiredService<ShellTheme>().Mode.ShouldBe(CmsThemeMode.Dark);
    }

    [Fact]
    public void WithOneCultureThereIsNoLanguageToChooseAndThePreferencesAreNotRead()
    {
        var app = Load();

        Section(app, "Theme").QuerySelectorAll("select").Count.ShouldBe(1);
        Management.Count("me/preferences").ShouldBe(0);
    }

    [Fact]
    public void TheLanguageIsSavedWithThePreferencesReadJustBefore()
    {
        OfferCultures();
        var app = Load();
        Section(app, "Language and theme").QuerySelectorAll("select")[1].GetAttribute("value").ShouldBe("en");

        // Another tab saved a time zone meanwhile.
        Management.Preferences = new JsonObject { ["culture"] = null, ["timeZone"] = "Europe/Berlin", ["dateFormat"] = "de-DE", ["numberFormat"] = null };
        Section(app, "Language and theme").QuerySelectorAll("select")[1].Change("fr");

        app.WaitForAssertion(() => Management.Preferences["culture"]!.GetValue<string>().ShouldBe("fr"));
        Management.Preferences["timeZone"]!.GetValue<string>().ShouldBe("Europe/Berlin");
        Management.Preferences["dateFormat"]!.GetValue<string>().ShouldBe("de-DE");
        Management.Calls.Where(call => call.Path == "me/preferences").Select(call => call.Method.Method).ShouldBe(["GET", "GET", "PUT"]);
        app.Find("[role=status][aria-live=polite]").TextContent.ShouldContain("The language is saved.");
    }

    [Fact]
    public void AFailedSaveShowsTheSavedLanguageAgain()
    {
        OfferCultures();
        Management.Answer = (method, path) => method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();

        Section(app, "Language and theme").QuerySelectorAll("select")[1].Change("fr");

        app.WaitForAssertion(() => Section(app, "Language and theme").TextContent.ShouldContain("The language could not be saved. Try again."));
        var select = Section(app, "Language and theme").QuerySelectorAll("select")[1];
        select.GetAttribute("value").ShouldBe("en");
        select.QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("en");
    }

    [Fact]
    public void TheLanguageOfThePreferencesIsShownByItsName()
    {
        OfferCultures();
        Management.Preferences["culture"] = "fr-CA";

        var app = Load();

        var language = Section(app, "Language and theme").QuerySelectorAll("select")[1];
        language.QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["English", "français"]);
        language.QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("fr");
    }

    [Fact]
    public void AKeyOrAServiceAccountGetsTheRefusalOfThePreferences()
    {
        OfferCultures();
        Management.Answer = (method, path) => path == "me/preferences" ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;

        var app = Load();

        Section(app, "Language and theme").TextContent.ShouldContain("Only a user has this.");
    }

    [Fact]
    public void TheBreadcrumbNamesThePage()
    {
        var app = Load();

        app.Find("nav[aria-label='Breadcrumb']").TextContent.ShouldContain("Account");
        app.Find("main h1").TextContent.ShouldBe("Your account");
    }

    private static JsonObject Login(string provider, string? displayName) => new()
    {
        ["provider"] = provider,
        ["providerKey"] = $"key-of-{provider}",
        ["displayName"] = displayName,
    };

    // A second culture, as config.json may offer once one ships.
    private void OfferCultures() => Services.AddSingleton(new InterfaceCultureOptions { Offered = ["en", "fr"], Default = "en" });

    private void AddSession(Guid id, string clientName, string createdAt = Morning) => Management.Sessions.Add(new JsonObject
    {
        ["id"] = id,
        ["clientId"] = "fluentcms-admin",
        ["clientName"] = clientName,
        ["createdAt"] = createdAt,
        ["scopes"] = new JsonArray("openid", "management"),
    });
}
