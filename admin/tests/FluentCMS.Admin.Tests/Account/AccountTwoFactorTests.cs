using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Account;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The two-factor section of the account page: enrolment, recovery codes, turning it off, and what they leave behind.
public sealed class AccountTwoFactorTests : AccountTestContext
{
    private const string Heading = "Two-factor sign-in";
    private const string SharedKey = "JBSWY3DPEHPK3PXP";
    private const string AuthenticatorUri = "otpauth://totp/FluentCMS:ada%40example.com?secret=" + SharedKey + "&issuer=FluentCMS&digits=6";
    private const string SignInAgainFirst = "Setting up two-factor sign-in needs a recent sign-in, and yours is more than a few minutes old. Sign out, sign in again, then set it up.";
    private static readonly string[] Codes = ["A1B2C-3D4E5", "F6G7H-8J9K0"];

    [Fact]
    public void AnEnrolmentShowsTheCodeAndKeyThenTheRecoveryCodesOnceThenSignsInAgain()
    {
        EnrolAnswers();
        var app = Load();
        Section(app, Heading).TextContent.ShouldContain("Two-factor sign-in is off.");

        Button(Section(app, Heading), "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("svg.qr-code").ShouldNotBeNull());
        var twoFactor = Section(app, Heading);
        var qr = twoFactor.QuerySelector("svg.qr-code")!;
        qr.GetAttribute("role").ShouldBe("img");
        qr.GetAttribute("aria-label").ShouldBe("QR code for the authenticator app. The key beside it holds the same.");
        // The modules and the quiet zone of four on each side, at 4 CSS pixels a module.
        var size = QrCode.Encode(AuthenticatorUri).Size + 8;
        qr.Attributes.Single(attribute => attribute.Name.Equals("viewBox", StringComparison.OrdinalIgnoreCase)).Value.ShouldBe($"0 0 {size} {size}");
        qr.GetAttribute("width").ShouldBe((size * 4).ToString(System.Globalization.CultureInfo.InvariantCulture));
        twoFactor.QuerySelector(".account-key")!.TextContent.ShouldBe("jbsw y3dp ehpk 3pxp");
        var input = twoFactor.QuerySelector("input")!;
        input.GetAttribute("autocomplete").ShouldBe("one-time-code");
        input.GetAttribute("inputmode").ShouldBe("numeric");
        app.WaitForAssertion(() => ShouldBeFocused<AccountTwoFactor>(app, "stepHeading"));
        twoFactor.TextContent.ShouldContain("Turning it on ends your sessions: after you have saved the recovery codes, you sign in again.");
        input.Input("123456");
        var reads = Management.Count("me/two-factor");
        Button(twoFactor, "Turn on").Click();

        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("pre.account-codes").ShouldNotBeNull());
        twoFactor = Section(app, Heading);
        twoFactor.QuerySelector("pre.account-codes")!.TextContent.ShouldBe(string.Join('\n', Codes));
        twoFactor.TextContent.ShouldContain("once you have saved the codes, you sign in again, with a code from the app");
        twoFactor.QuerySelector("fluent-dialog")!.GetAttribute("aria-label").ShouldBe("Save your recovery codes");
        twoFactor.QuerySelector("svg.qr-code").ShouldBeNull();
        app.Markup.ShouldNotContain(SharedKey, Case.Insensitive);
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == "me/two-factor/verify" && call.Body == """{"code":"123456"}""");
        SignIns.ShouldBeEmpty();

        Button(twoFactor, "I have saved them").Click();

        app.WaitForAssertion(() => SignIns.ShouldBe([AccountPage.Route]));
        // Nothing was read while the codes were shown, and the status is the one the verify gave.
        Management.Count("me/two-factor").ShouldBe(reads);
        Section(app, Heading).TextContent.ShouldContain("Two-factor sign-in is on. Recovery codes left: 2.");
        Message(app, Heading).ShouldBe("The recovery codes are no longer shown.");
        AssertNoSecretShown(app);
        AssertNoSecretKept();
    }

    [Fact]
    public void TheRecoveryCodesAreNotShownAgainOnASecondVisit()
    {
        EnrolAnswers();
        var app = Load();
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "Turn on").Click();
        app.WaitForAssertion(() => app.Markup.ShouldContain(Codes[0]));

        Navigation.NavigateTo("");
        app.WaitForAssertion(() => app.FindAll(".account").ShouldBeEmpty());
        Navigation.NavigateTo(AccountPage.Route);

        app.WaitForAssertion(() => Section(app, Heading).TextContent.ShouldContain("Recovery codes left: 10."));
        AssertNoSecretShown(app);
        AssertNoSecretKept();
    }

    [Fact]
    public void AWrongCodeKeepsTheStepAnnouncesTheErrorAndLeavingTheStepDropsTheKey()
    {
        EnrolAnswers(wrongCode: true);
        var app = Load();
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());

        Section(app, Heading).QuerySelector("input")!.Input("000000");
        Button(Section(app, Heading), "Turn on").Click();

        app.WaitForAssertion(() => Section(app, Heading).TextContent.ShouldContain("The code is not right"));
        Section(app, Heading).QuerySelector("input")!.GetAttribute("aria-invalid").ShouldBe("true");
        Section(app, Heading).QuerySelector("svg.qr-code").ShouldNotBeNull();
        app.Find("[role=alert]").TextContent.ShouldContain("The code is not right");

        Button(Section(app, Heading), "Cancel").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Nothing was changed."));
        AssertNoSecretShown(app);
        Management.Count("me/two-factor/verify").ShouldBe(1);
        app.WaitForAssertion(() => ShouldBeFocused<AccountTwoFactor>(app, "messageElement"));
    }

    [Fact]
    public void AnEmptyCodeIsNotSentAndIsAnnounced()
    {
        EnrolAnswers();
        var app = Load();
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());

        Button(Section(app, Heading), "Turn on").Click();

        app.WaitForAssertion(() => Section(app, Heading).TextContent.ShouldContain("Enter a code."));
        app.Find("[role=alert]").TextContent.ShouldContain("Enter a code.");
        Management.Count("me/two-factor/verify").ShouldBe(0);
    }

    [Fact]
    public void AKeyTooLongForAQrCodeIsShownToEnterByHand()
    {
        // The issuer is written twice, escaped: a brand of 40 Persian letters makes the URI longer than the largest code.
        var issuer = Uri.EscapeDataString(new string('پ', 40));
        var uri = $"otpauth://totp/{issuer}:ada%40example.com?secret={SharedKey}&issuer={issuer}&digits=6";
        QrCode.Fits(uri).ShouldBeFalse();
        EnrolAnswers(uri: uri);
        var app = Load();

        Button(Section(app, Heading), "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Section(app, Heading).QuerySelector(".account-key").ShouldNotBeNull());
        var twoFactor = Section(app, Heading);
        twoFactor.QuerySelector("svg").ShouldBeNull();
        twoFactor.TextContent.ShouldContain("The address for the app, with the site's name in it, is too long for a QR code. Enter this key in the app by hand");
        twoFactor.QuerySelector(".account-key")!.TextContent.ShouldBe("jbsw y3dp ehpk 3pxp");
        twoFactor.QuerySelector("input").ShouldNotBeNull();
    }

    [Fact]
    public void EnrollingWhileItIsOnAlreadyReadsTheStatusAgain()
    {
        Management.Answer = (method, path) =>
        {
            if (path == "me/two-factor/enrol")
            {
                Management.TwoFactor = Status(enabled: true, left: 5);
                return Problem(HttpStatusCode.Conflict, "FCMS3060", "twoFactor");
            }

            return null;
        };
        var app = Load();

        Button(Section(app, Heading), "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Two-factor sign-in is on already."));
        Section(app, Heading).TextContent.ShouldContain("Recovery codes left: 5.");
    }

    [Theory]
    [InlineData("me/two-factor/verify", "FCMS3062")]
    [InlineData("me/two-factor/verify", "FCMS3060")]
    public void AVerifyRefusedForAChangedStateClosesTheStepAndReadsTheStatus(string path, string code)
    {
        EnrolAnswers();
        var enrol = Management.Answer!;
        Management.Answer = (method, called) => called == path ? Problem(HttpStatusCode.UnprocessableEntity, code, "code") : enrol(method, called);
        var app = Load();
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        var reads = Management.Count("me/two-factor");

        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "Turn on").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Two-factor sign-in was changed meanwhile, so nothing was done. Its status is shown again."));
        Management.Count("me/two-factor").ShouldBe(reads + 1);
        AssertNoSecretShown(app);
    }

    // The server asks for a sign-in of the last minutes before it enrols an authenticator or turns two-factor on (#3372).
    [Fact]
    public void AnEnrolmentWithASignInTooOldSaysToSignInAgain()
    {
        Management.Answer = (method, path) => path == "me/two-factor/enrol" ? Problem(HttpStatusCode.Forbidden, "FCMS3065") : null;
        var app = Load();

        Button(Section(app, Heading), "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe(SignInAgainFirst));
        app.WaitForAssertion(() => ShouldBeFocused<AccountTwoFactor>(app, "messageElement"));
        Section(app, Heading).QuerySelector("input").ShouldBeNull();
    }

    [Fact]
    public void ATurnOnWithASignInTooOldClosesTheStepAndSaysToSignInAgain()
    {
        EnrolAnswers();
        var enrol = Management.Answer!;
        Management.Answer = (method, called) => called == "me/two-factor/verify" ? Problem(HttpStatusCode.Forbidden, "FCMS3065") : enrol(method, called);
        var app = Load();
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());

        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "Turn on").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe(SignInAgainFirst));
        AssertNoSecretShown(app);
    }

    [Fact]
    public void AFailedEnrolmentIsSaid()
    {
        Management.Answer = (method, path) => path == "me/two-factor/enrol" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();

        Button(Section(app, Heading), "Set up two-factor sign-in").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("That did not work. The server did not answer, or answered with an error."));
        app.WaitForAssertion(() => ShouldBeFocused<AccountTwoFactor>(app, "messageElement"));
    }

    [Fact]
    public void TwoFactorIsTurnedOffWithARecoveryCodeAndTheTabSignsInAgain()
    {
        Management.TwoFactor = Status(enabled: true, left: 3, required: true);
        Management.Answer = (method, path) => path == "me/two-factor/disable" ? NoContent() : null;
        var app = Load();
        Section(app, Heading).TextContent.ShouldContain("Recovery codes left: 3.");

        Button(Section(app, Heading), "Turn off").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        var twoFactor = Section(app, Heading);
        twoFactor.TextContent.ShouldContain("Turning it off ends your sessions, so you sign in again afterwards.");
        twoFactor.TextContent.ShouldContain("A role of yours requires two-factor sign-in");
        twoFactor.QuerySelector("label")!.TextContent.ShouldContain("Code from the app, or a recovery code");
        var input = twoFactor.QuerySelector("input")!;
        input.GetAttribute("autocomplete").ShouldBe("off");
        input.HasAttribute("inputmode").ShouldBeFalse();
        input.Input(Codes[0]);
        var reads = Management.Count("me/two-factor");
        Button(twoFactor, "Turn off").Click();

        app.WaitForAssertion(() => SignIns.ShouldBe([AccountPage.Route]));
        Message(app, Heading).ShouldBe("Two-factor sign-in is off.");
        Section(app, Heading).TextContent.ShouldContain("Two-factor sign-in is off. With it on");
        Management.Count("me/two-factor").ShouldBe(reads);
        Management.Calls.ShouldContain(call => call.Path == "me/two-factor/disable" && call.Body == $$"""{"code":"{{Codes[0]}}"}""");
    }

    [Fact]
    public void TurningOffWhenItIsOffAlreadyReadsTheStatusAndDoesNotSignInAgain()
    {
        Management.TwoFactor = Status(enabled: true, left: 3);
        Management.Answer = (method, path) =>
        {
            if (path == "me/two-factor/disable")
            {
                Management.TwoFactor = Status(enabled: false, left: 0);
                return Problem(HttpStatusCode.Conflict, "FCMS3061", "twoFactor");
            }

            return null;
        };
        var app = Load();
        Button(Section(app, Heading), "Turn off").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());

        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "Turn off").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Two-factor sign-in was changed meanwhile, so nothing was done. Its status is shown again."));
        Section(app, Heading).TextContent.ShouldContain("Two-factor sign-in is off. With it on");
        SignIns.ShouldBeEmpty();
    }

    [Fact]
    public void NewRecoveryCodesTakeAnAuthenticatorCodeAreShownOnceAndKeepTheSession()
    {
        Management.TwoFactor = Status(enabled: true, left: 1);
        Management.Answer = (method, path) => path == "me/two-factor/recovery-codes"
            ? Json(new JsonObject { ["recoveryCodes"] = new JsonArray([.. Codes.Select(code => (JsonNode)code)]) })
            : null;
        var app = Load();

        Button(Section(app, Heading), "New recovery codes").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        var input = Section(app, Heading).QuerySelector("input")!;
        input.GetAttribute("autocomplete").ShouldBe("one-time-code");
        input.GetAttribute("inputmode").ShouldBe("numeric");
        input.Input("123456");
        Button(Section(app, Heading), "New recovery codes").Click();

        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("pre.account-codes")!.TextContent.ShouldBe(string.Join('\n', Codes)));
        Section(app, Heading).TextContent.ShouldNotContain("you sign in again");
        Button(Section(app, Heading), "I have saved them").Click();

        app.WaitForAssertion(() => AssertNoSecretShown(app));
        Section(app, Heading).TextContent.ShouldContain("Recovery codes left: 2.");
        SignIns.ShouldBeEmpty();
    }

    [Fact]
    public void ARoleThatRequiresTwoFactorAndASetupNotFinishedAreSaid()
    {
        Management.TwoFactor = Status(enabled: false, left: 0, required: true, pending: true);

        var app = Load();

        var twoFactor = Section(app, Heading).TextContent;
        twoFactor.ShouldContain("A role of yours requires it");
        twoFactor.ShouldContain("A setup was started and not finished.");
    }

    [Fact]
    public void WhileTheCodesAreShownAndTheSignInIsOnItsWayTheRestOfThePageIsInert()
    {
        EnrolAnswers();
        Management.Sessions.Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientName"] = "Phone app" });
        Management.Logins.Add(new JsonObject { ["provider"] = "corp", ["providerKey"] = "1", ["displayName"] = "Corporate sign-in" });
        var app = Load();
        TurnOn(app);
        var calls = Management.Calls.Count;

        AssertInert(app);
        Button(Section(app, "Sessions"), "End, the sign-in of Phone app").Click();
        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").Click();
        Section(app, "Theme").QuerySelector("select")!.Change("Dark");

        Management.Calls.Count.ShouldBe(calls);
        SignIns.ShouldBeEmpty();
        JSInterop.Invocations.ShouldNotContain(call => call.Identifier == "localStorage.setItem");

        // Until the page has left for the sign-in.
        Button(Section(app, Heading), "I have saved them").Click();
        app.WaitForAssertion(() => SignIns.ShouldBe([AccountPage.Route]));
        AssertInert(app);
    }

    [Fact]
    public void NewCodesLetThePageGoOnceSaved()
    {
        Management.TwoFactor = Status(enabled: true, left: 1);
        Management.Logins.Add(new JsonObject { ["provider"] = "corp", ["providerKey"] = "1", ["displayName"] = "Corporate sign-in" });
        Management.Answer = (method, path) => path == "me/two-factor/recovery-codes"
            ? Json(new JsonObject { ["recoveryCodes"] = new JsonArray([.. Codes.Select(code => (JsonNode)code)]) })
            : null;
        var app = Load();
        Button(Section(app, Heading), "New recovery codes").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "New recovery codes").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("pre.account-codes").ShouldNotBeNull());
        AssertInert(app);

        Button(Section(app, Heading), "I have saved them").Click();

        app.WaitForAssertion(() => Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").HasAttribute("disabled").ShouldBeFalse());
        Section(app, "Password").QuerySelector("a")!.HasAttribute("href").ShouldBeTrue();
    }

    [Fact]
    public void ASignInThatCannotStartAfterTheCodesLetsThePageGoAndSaysSo()
    {
        EnrolAnswers();
        Management.Logins.Add(new JsonObject { ["provider"] = "corp", ["providerKey"] = "1", ["displayName"] = "Corporate sign-in" });
        var app = Load();
        FailSignIn();
        TurnOn(app);

        Button(Section(app, Heading), "I have saved them").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("The sign-in server could not be reached to sign you in again. Reload the page to sign in."));
        Button(Section(app, "External logins"), "Remove, the login of Corporate sign-in").HasAttribute("disabled").ShouldBeFalse();
        AssertNoSecretShown(app);
    }

    [Fact]
    public void ASignInThatCannotStartAfterTurningOffIsSaid()
    {
        Management.TwoFactor = Status(enabled: true, left: 3);
        Management.Answer = (method, path) => path == "me/two-factor/disable" ? NoContent() : null;
        var app = Load();
        FailSignIn();
        Button(Section(app, Heading), "Turn off").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        Section(app, Heading).QuerySelector("input")!.Input(Codes[0]);

        Button(Section(app, Heading), "Turn off").Click();

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("The sign-in server could not be reached to sign you in again. Reload the page to sign in."));
        Section(app, Heading).TextContent.ShouldContain("Two-factor sign-in is off. With it on");
    }

    private static void AssertInert(IRenderedComponent<AdminApp> app)
    {
        foreach (var heading in new[] { "Sessions", "External logins" })
        {
            Section(app, heading).QuerySelectorAll("fluent-button").ShouldAllBe(button => button.HasAttribute("disabled"));
        }

        Section(app, "Theme").QuerySelector("select")!.HasAttribute("disabled").ShouldBeTrue();
        Section(app, "Password").QuerySelector("a")!.HasAttribute("href").ShouldBeFalse();
    }

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

    // Every text an argument holds, inside arrays and objects too.
    private static IEnumerable<string> Texts(object? argument) => argument switch
    {
        null => [],
        string text => [text],
        System.Collections.IEnumerable items => items.Cast<object?>().SelectMany(Texts),
        _ => [argument.ToString() ?? "", Serialized(argument)],
    };

    private static string Serialized(object argument)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(argument, argument.GetType());
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return "";
        }
    }

    private static bool HoldsASecret(string value) =>
        value.Contains(SharedKey, StringComparison.OrdinalIgnoreCase)
        || value.Contains("jbsw y3dp ehpk 3pxp", StringComparison.OrdinalIgnoreCase)
        || Codes.Any(code => value.Contains(code, StringComparison.OrdinalIgnoreCase));

    // Nothing of the enrolment reached the URL, the tab's storage or a call to the browser, in either form of the key, whole
    // or within a larger value.
    private void AssertNoSecretKept()
    {
        HoldsASecret(Navigation.Uri).ShouldBeFalse();
        Storage.Items.Values.Where(HoldsASecret).ShouldBeEmpty();
        JSInterop.Invocations.SelectMany(invocation => invocation.Arguments).SelectMany(Texts).Where(HoldsASecret).ShouldBeEmpty();
    }

    private static void TurnOn(IRenderedComponent<AdminApp> app)
    {
        Button(Section(app, Heading), "Set up two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("input").ShouldNotBeNull());
        Section(app, Heading).QuerySelector("input")!.Input("123456");
        Button(Section(app, Heading), "Turn on").Click();
        app.WaitForAssertion(() => Section(app, Heading).QuerySelector("pre.account-codes").ShouldNotBeNull());
    }

    private void EnrolAnswers(bool wrongCode = false, string uri = AuthenticatorUri) => Management.Answer = (method, path) =>
    {
        switch (path)
        {
            case "me/two-factor/enrol":
                Management.TwoFactor = Status(enabled: false, left: 0, pending: true);
                return Json(new JsonObject { ["sharedKey"] = SharedKey, ["authenticatorUri"] = uri });
            case "me/two-factor/verify" when wrongCode:
                return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3063", "code");
            case "me/two-factor/verify":
                Management.TwoFactor = Status(enabled: true, left: 10);
                return Json(new JsonObject { ["recoveryCodes"] = new JsonArray([.. Codes.Select(code => (JsonNode)code)]) });
            default:
                return null;
        }
    };
}
