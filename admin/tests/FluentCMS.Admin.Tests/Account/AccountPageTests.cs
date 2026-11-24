using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Administration.Account;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The sessions, external logins, password link, theme, language, time zone and formats of the account page.
public sealed class AccountPageTests : AccountTestContext
{
    private static readonly Guid Admin = new("0193a1b2-0000-7000-8000-0000000000c1");
    private static readonly Guid Phone = new("0193a1b2-0000-7000-8000-0000000000c2");
    private const string Morning = "2026-11-16T09:30:00+00:00";
    private const string Evening = "2026-11-15T18:05:00+00:00";
    private const string Card = "Preferences";

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
        Section(app, "External logins").TextContent.ShouldContain("Removing one ends your sessions, so you sign in again at once.");

        Button(Section(app, "External logins"), "Remove, the login of github").Click();

        app.WaitForAssertion(() => SignIns.ShouldBe([AccountPage.Route]));
        Message(app, "External logins").ShouldBe("The login of github is removed.");
    }

    [Fact]
    public void ASignInThatCannotStartAfterARemovalIsSaid()
    {
        Management.Logins.Add(Login("github", null));
        Management.Answer = (method, path) => method == HttpMethod.Delete ? NoContent() : null;
        var app = Load();
        FailSignIn();

        Button(Section(app, "External logins"), "Remove, the login of github").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("The sign-in server could not be reached to sign you in again. Reload the page to sign in."));
        Section(app, "External logins").QuerySelector("fluent-button")!.HasAttribute("disabled").ShouldBeFalse();
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

        Select(app, "Theme").Change("Dark");

        app.WaitForAssertion(() => app.Find(".cms-root").GetAttribute("data-cms-theme").ShouldBe("dark"));
        JSInterop.VerifyInvoke("localStorage.setItem").Arguments.ShouldBe([ThemeStorageInterop.Key, "dark"]);
        Services.GetRequiredService<ShellTheme>().Mode.ShouldBe(CmsThemeMode.Dark);
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Patch);
    }

    // In the order the keyboard reaches them, each with its label and, but for the theme and the language, its example; the
    // language only when more than one culture is offered.
    [Fact]
    public void WithOneCultureTheCardOffersTheThemeTheTimeZoneAndTheFormatsInTheirOrder()
    {
        var app = Load();

        var card = Section(app, Card);
        card.QuerySelectorAll("label").Select(label => label.TextContent).ShouldBe(["Theme", "Time zone", "Format of dates and times", "Format of numbers"]);
        card.QuerySelectorAll("select, fluent-button").Select(control => control.LocalName).ShouldBe(["select", "select", "select", "select", "fluent-button"]);
        card.QuerySelectorAll("[tabindex='-1'], [disabled]").ShouldBeEmpty();
        foreach (var label in new[] { "Time zone", "Format of dates and times", "Format of numbers" })
        {
            var select = Select(app, label);
            select.QuerySelector("option[value='']")!.TextContent.ShouldBe("As the browser");
            card.QuerySelector($"#{select.GetAttribute("aria-describedby")}")!.TextContent.ShouldNotBeNullOrWhiteSpace();
        }

        Management.Count("me/preferences").ShouldBe(2);
    }

    [Fact]
    public void TheTimeZoneAndTheFormatsShowTheirExampleAsChosenAndApplyAfterTheSaveWithoutAReload()
    {
        var now = new DateTimeOffset(2026, 11, 16, 23, 45, 0, TimeSpan.Zero);
        Services.AddSingleton<TimeProvider>(new MovableTime { Now = now });
        AddSession(Admin, "FluentCMS admin");
        var app = Load();
        var before = When(Morning);
        var (german, french) = (CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("fr-FR"));
        var tokyo = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).DateTime;

        Select(app, "Time zone").Change("Asia/Tokyo");
        Select(app, "Format of dates and times").Change("de-DE");
        Select(app, "Format of numbers").Change("fr-FR");

        Example(app, "Time zone").ShouldBe($"It is now {tokyo.ToString("t", german)} in this time zone.");
        Example(app, "Format of dates and times").ShouldBe($"Today shows as {tokyo.ToString("d", german)}, and this time as {tokyo.ToString("t", german)}.");
        Example(app, "Format of numbers").ShouldBe($"A number shows as {1234567.89m.ToString("#,0.##", french)}.");
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Patch);

        Button(Section(app, Card), "Save preferences").Click();

        app.WaitForAssertion(() => Message(app, Card).ShouldBe("The preferences are saved."));
        Saves().ShouldBe([new JsonObject { ["timeZone"] = "Asia/Tokyo", ["dateFormat"] = "de-DE", ["numberFormat"] = "fr-FR" }.ToJsonString()]);
        var formats = Services.GetRequiredService<FormatService>();
        (formats.ZoneId, formats.Culture.Name, formats.NumberCulture.Name).ShouldBe(("Asia/Tokyo", "de-DE", "fr-FR"));
        When(Morning).ShouldNotBe(before);
        app.WaitForAssertion(() => Names(Section(app, "Sessions")).ShouldBe(["FluentCMS admin"]));
        Section(app, "Sessions").TextContent.ShouldContain(When(Morning));
        app.Find("[role=status][aria-live=polite]").TextContent.ShouldContain("The preferences are saved.");
        Reloads().ShouldBeEmpty();
    }

    [Fact]
    public void AListShowsTheFormatsSavedHereWithoutAReload()
    {
        var starts = new DateTimeOffset(2026, 11, 15, 21, 30, 0, TimeSpan.Zero);
        Management.SetTypes(MainSite, Type("article", fields: [Field("title"), Field("startsAt", "dateTime")]));
        Management.Entries["article"] = [new JsonObject { ["id"] = "0193a1b2-0000-7000-8000-000000000001", ["status"] = "draft", ["locale"] = "en", ["version"] = 1, ["title"] = "Hello", ["startsAt"] = "2026-11-15T21:30:00Z" }];
        var app = Load();

        Select(app, "Time zone").Change("Pacific/Kiritimati");
        Select(app, "Format of dates and times").Change("de-DE");
        Button(Section(app, Card), "Save preferences").Click();
        app.WaitForAssertion(() => Message(app, Card).ShouldBe("The preferences are saved."));
        Navigation.NavigateTo("content/article?columns=title,startsAt");

        var expected = TimeZoneInfo.ConvertTime(starts, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati")).DateTime.ToString("g", CultureInfo.GetCultureInfo("de-DE"));
        app.WaitForAssertion(() => app.Find("main tbody time").TextContent.ShouldBe(expected));
        Reloads().ShouldBeEmpty();
    }

    // Only the members that changed are sent, so the time zone another tab saved meanwhile stays; the app then reloads in the
    // new language, and the browser keeps it for the next start.
    [Fact]
    public void TheLanguageIsSavedAloneAndTheAppReloadsInIt()
    {
        OfferCultures();
        var app = Load();
        Select(app, "Interface language").GetAttribute("value").ShouldBe("en");

        Management.Preferences = new JsonObject { ["culture"] = null, ["timeZone"] = "Europe/Berlin", ["dateFormat"] = "de-DE", ["numberFormat"] = null };
        Select(app, "Interface language").Change("fr");
        Button(Section(app, Card), "Save preferences").Click();

        app.WaitForAssertion(() => Reloads().ShouldHaveSingleItem().ShouldEndWith(AccountPage.Route));
        Saves().ShouldBe(["""{"culture":"fr"}"""]);
        Management.Preferences.ToJsonString().ShouldBe("""{"culture":"fr","timeZone":"Europe/Berlin","dateFormat":"de-DE","numberFormat":null}""");
        JSInterop.Invocations.Where(call => call.Identifier == "localStorage.setItem").Select(call => call.Arguments.ToArray())
            .ShouldContain(arguments => arguments.SequenceEqual(new object?[] { CultureStorageInterop.Key, "fr" }));
        Services.GetRequiredService<FormatService>().ZoneId.ShouldBe("Europe/Berlin");
    }

    [Fact]
    public void AFailedSaveKeepsTheChoiceAndSaysSo()
    {
        OfferCultures();
        Management.Answer = (method, path) => method == HttpMethod.Patch ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();

        Select(app, "Interface language").Change("fr");
        Button(Section(app, Card), "Save preferences").Click();

        app.WaitForAssertion(() => Message(app, Card).ShouldBe("The preferences could not be saved. Try again."));
        app.Find("[role=alert]").TextContent.ShouldContain("The preferences could not be saved. Try again.");
        var language = Select(app, "Interface language");
        language.HasAttribute("disabled").ShouldBeFalse();
        language.GetAttribute("value").ShouldBe("fr");
        Reloads().ShouldBeEmpty();
    }

    [Fact]
    public void AValueTheServerRefusesIsNamedOnItsField()
    {
        Management.Answer = (method, path) => method == HttpMethod.Patch ? Problem(HttpStatusCode.BadRequest, "FCMS1001", "timeZone") : null;
        var app = Load();

        Select(app, "Time zone").Change("Asia/Tokyo");
        Select(app, "Format of numbers").Change("fr-FR");
        Button(Section(app, Card), "Save preferences").Click();

        app.WaitForAssertion(() => Message(app, Card).ShouldBe("The preferences are not saved: the server does not know a value."));
        var zone = Select(app, "Time zone");
        zone.GetAttribute("aria-invalid").ShouldBe("true");
        Section(app, Card).QuerySelector($"#{zone.GetAttribute("aria-describedby")}")!.TextContent.ShouldBe("The server does not know this value. Choose another.");
        Select(app, "Format of numbers").HasAttribute("aria-invalid").ShouldBeFalse();
        Select(app, "Format of numbers").GetAttribute("value").ShouldBe("fr-FR");
        Services.GetRequiredService<FormatService>().NumberCulture.Name.ShouldNotBe("fr-FR");
    }

    // A zone or a culture saved elsewhere that the browser does not list, as a neutral culture, shows as it is saved rather
    // than as the browser's.
    [Fact]
    public void ASavedValueTheBrowserDoesNotListIsShownAsSaved()
    {
        Management.Preferences["timeZone"] = "Mars/Olympus";
        Management.Preferences["numberFormat"] = "de";

        var app = Load();

        var zone = Select(app, "Time zone");
        zone.GetAttribute("value").ShouldBe("Mars/Olympus");
        zone.QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("Mars/Olympus");
        Select(app, "Format of numbers").QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("de");
    }

    [Fact]
    public void TheLanguageOfThePreferencesIsShownByItsName()
    {
        OfferCultures();
        Management.Preferences["culture"] = "fr-CA";
        UseCultureCopy("fr-CA");

        var app = Load();

        var language = Select(app, "Interface language");
        language.QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["English", "français"]);
        language.QuerySelector("option[selected]")!.GetAttribute("value").ShouldBe("fr");
    }

    [Fact]
    public void AKeyOrAServiceAccountGetsTheRefusalOfThePreferences()
    {
        OfferCultures();
        Management.Answer = (method, path) => path == "me/preferences" ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;

        var app = Load();

        Section(app, Card).TextContent.ShouldContain("Only a user has this.");
        Section(app, Card).QuerySelectorAll("select").Length.ShouldBe(1);
    }

    // The page is in the administration assembly, which its first visit fetches with the shell's loading state (A-41), and
    // the user menu's link reaches it.
    [Fact]
    public void TheFirstVisitLoadsThePageWithTheAdministrationAssemblyAndShowsTheLoadingStateMeanwhile()
    {
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.AddScoped<IAdminAssemblyLoader>(_ => Loader);
        Start();
        Navigation.NavigateTo(AccountPage.Route);
        var app = Render<AdminApp>();

        app.WaitForAssertion(() => app.Find("main .shell-notice[role=status]").TextContent.ShouldBe("Loading the page…"));
        hold.SetResult();

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Your account"));
        app.FindAll("main .shell-notice[role=status]").ShouldBeEmpty();
        Loader.Loads.ShouldBe(1);
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

    private static IElement Select(IRenderedComponent<AdminApp> app, string label) =>
        Section(app, Card).QuerySelectorAll(".cms-field").Single(field => field.QuerySelector("label")!.TextContent == label).QuerySelector("select")!;

    // The text the select is described by: its example, or its error.
    private static string Example(IRenderedComponent<AdminApp> app, string label) =>
        Section(app, Card).QuerySelector($"#{Select(app, label).GetAttribute("aria-describedby")}")!.TextContent;

    // The bodies of the changes of the preferences the card sent.
    private List<string?> Saves() => [.. Management.Calls.Where(call => call.Method == HttpMethod.Patch && call.Path == "me/preferences").Select(call => call.Body)];

    // The addresses the whole page was loaded again at.
    private List<string> Reloads() => [.. Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().History.Where(entry => entry.Options.ForceLoad).Select(entry => entry.Uri)];

    private void AddSession(Guid id, string clientName, string createdAt = Morning) => Management.Sessions.Add(new JsonObject
    {
        ["id"] = id,
        ["clientId"] = "fluentcms-admin",
        ["clientName"] = clientName,
        ["createdAt"] = createdAt,
        ["scopes"] = new JsonArray("openid", "management"),
    });
}
