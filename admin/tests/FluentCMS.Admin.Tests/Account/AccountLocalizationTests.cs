using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The account page in each state that shows texts of its own, under both pseudo-cultures. The data the fake serves is marked
// as a resource is, or holds no letter, so that only the page's own texts are checked. The language select is left out: with
// one shipped culture it is not shown, and another culture's name is data.
public sealed class AccountLocalizationTests : AdminTestContext
{
    private static readonly string[] StateNames =
    [
        "Lists", "Empty", "Enrol", "KeyOnly", "WrongCode", "EmptyCode", "Codes", "Cancelled", "On", "NewCodes", "NewCodesShown",
        "Disable", "DisableRequired", "RequiredAndPending", "Refused", "Failed", "ThisTabEnded", "Ended", "AlreadyEnded",
        "EndFailed", "Removed", "LastWayIn", "AlreadyRemoved", "RemoveFailed", "AlreadyOn", "Changed", "SignInFailed",
    ];

    public static TheoryData<string, string> States()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { PseudoCulture.LeftToRight, PseudoCulture.RightToLeft })
        {
            foreach (var state in StateNames)
            {
                data.Add(culture, state);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(States))]
    public void EveryTextOfTheAccountPageComesFromTheResources(string culture, string state)
    {
        UseInterfaceCulture(culture);
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        if (state != "Empty")
        {
            Management.Sessions.Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientName"] = PseudoCulture.Transform("Admin", rightToLeft) });
            Management.Logins.Add(new JsonObject { ["provider"] = "corp", ["providerKey"] = "1", ["displayName"] = PseudoCulture.Transform("Corp", rightToLeft) });
        }

        var key = "2345672345672345";
        var uri = state == "KeyOnly" ? "otpauth://totp/" + new string('7', 420) : $"otpauth://totp/x?secret={key}";
        var ended = false;
        Management.Answer = (method, path) => (state, path) switch
        {
            ("Refused", "me/two-factor" or "me/logins" or "me/preferences") => Problem(HttpStatusCode.Forbidden, "FCMS2001"),
            ("Failed", "me/sessions" or "me/two-factor" or "me/logins" or "me/preferences") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            ("ThisTabEnded", "me/sessions") when ended => Problem(HttpStatusCode.Unauthorized, "FCMS3102"),
            ("ThisTabEnded", _) when method == HttpMethod.Delete => End(),
            ("AlreadyEnded", _) when method == HttpMethod.Delete => Problem(HttpStatusCode.NotFound, "FCMS3047"),
            ("EndFailed" or "RemoveFailed", _) when method == HttpMethod.Delete => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            ("LastWayIn", _) when method == HttpMethod.Delete => Problem(HttpStatusCode.Conflict, "FCMS3090", "provider"),
            ("AlreadyRemoved", _) when method == HttpMethod.Delete => Problem(HttpStatusCode.UnprocessableEntity, "FCMS3091", "provider"),
            ("AlreadyOn", "me/two-factor/enrol") => Problem(HttpStatusCode.Conflict, "FCMS3060", "twoFactor"),
            ("Changed", "me/two-factor/verify") => Problem(HttpStatusCode.UnprocessableEntity, "FCMS3062", "code"),
            (_, "me/two-factor/enrol") => Json(new JsonObject { ["sharedKey"] = key, ["authenticatorUri"] = uri }),
            ("WrongCode", "me/two-factor/verify") => Problem(HttpStatusCode.UnprocessableEntity, "FCMS3063", "code"),
            (_, "me/two-factor/verify" or "me/two-factor/recovery-codes") => Json(new JsonObject { ["recoveryCodes"] = new JsonArray("12345-67890", "23456-78901") }),
            (_, _) when method == HttpMethod.Delete => NoContent(),
            _ => null,
        };
        if (state is "On" or "Disable" or "DisableRequired" or "NewCodes" or "NewCodesShown")
        {
            Management.TwoFactor["enabled"] = true;
            Management.TwoFactor["recoveryCodesLeft"] = 2;
        }

        if (state is "DisableRequired" or "RequiredAndPending")
        {
            Management.TwoFactor["required"] = true;
            Management.TwoFactor["pending"] = state == "RequiredAndPending";
        }

        Start();
        if (state == "SignInFailed")
        {
            ((Authentication.SignedInTestSession)Services.GetRequiredService<FluentCMS.Admin.Authentication.IAdminSession>()).SignInError = new HttpRequestException("No answer.");
        }

        Services.GetRequiredService<NavigationManager>().NavigateTo(AccountPage.Route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll("section.cms-card").Count.ShouldBe(5));
        app.WaitForAssertion(() => app.FindAll("section.cms-card [role=status]").ShouldBeEmpty());

        switch (state)
        {
            case "Enrol" or "KeyOnly" or "WrongCode" or "EmptyCode" or "Codes" or "Cancelled" or "Changed":
                Click(app, 1, 0);
                app.WaitForAssertion(() => app.Find("section.cms-card .account-key"));
                if (state == "Cancelled")
                {
                    Click(app, 1, 1);
                }
                else if (state is "WrongCode" or "EmptyCode" or "Codes" or "Changed")
                {
                    if (state != "EmptyCode")
                    {
                        app.Find("section.cms-card input").Input("123456");
                    }

                    Click(app, 1, 0);
                }

                app.WaitForAssertion(() => app.Find(state switch
                {
                    "Codes" => "pre.account-codes",
                    "WrongCode" or "EmptyCode" => "input[aria-invalid=true]",
                    "Cancelled" or "Changed" => ".account-message:not(:empty)",
                    _ => ".account-key",
                }));
                break;
            case "AlreadyOn":
                Click(app, 1, 0);
                app.WaitForAssertion(() => app.Find(".account-message:not(:empty)"));
                break;
            case "NewCodes" or "NewCodesShown" or "Disable" or "DisableRequired":
                Click(app, 1, state.StartsWith("NewCodes", StringComparison.Ordinal) ? 0 : 1);
                app.WaitForAssertion(() => app.Find("section.cms-card input"));
                if (state == "NewCodesShown")
                {
                    app.Find("section.cms-card input").Input("123456");
                    Click(app, 1, 0);
                    app.WaitForAssertion(() => app.Find("pre.account-codes"));
                }

                break;
            case "ThisTabEnded":
                Click(app, 0, 0);
                app.WaitForAssertion(() => app.FindAll("section.cms-card")[0].QuerySelector("[role=status]").ShouldNotBeNull());
                break;
            case "Ended" or "AlreadyEnded" or "EndFailed":
                Click(app, 0, 0);
                app.WaitForAssertion(() => app.FindAll("section.cms-card")[0].QuerySelector(".account-message:not(:empty)").ShouldNotBeNull());
                break;
            case "Removed" or "SignInFailed" or "LastWayIn" or "AlreadyRemoved" or "RemoveFailed":
                Click(app, 2, 0);
                app.WaitForAssertion(() => app.FindAll("section.cms-card")[2].QuerySelector(".account-message:not(:empty)").ShouldNotBeNull());
                break;
        }

        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();

        HttpResponseMessage End()
        {
            ended = true;
            return NoContent();
        }
    }

    private static void Click(IRenderedComponent<AdminApp> app, int section, int button) =>
        app.FindAll("section.cms-card")[section].QuerySelectorAll("fluent-button")[button].Click();
}
