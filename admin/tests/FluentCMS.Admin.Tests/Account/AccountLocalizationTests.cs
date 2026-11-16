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
// as a resource is, or holds no letter, so that only the page's own texts are checked.
public sealed class AccountLocalizationTests : AdminTestContext
{
    public static TheoryData<string, string> States()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { PseudoCulture.LeftToRight, PseudoCulture.RightToLeft })
        {
            foreach (var state in new[] { "Lists", "Enrol", "WrongCode", "Codes", "On", "Disable", "Refused", "Failed", "ThisTabEnded" })
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
        Management.Sessions.Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientName"] = PseudoCulture.Transform("Admin", rightToLeft) });
        Management.Logins.Add(new JsonObject { ["provider"] = "corp", ["providerKey"] = "1", ["displayName"] = PseudoCulture.Transform("Corp", rightToLeft) });
        var ended = false;
        Management.Answer = (method, path) => (state, path) switch
        {
            ("Refused", "me/two-factor" or "me/logins" or "me/preferences") => Problem(HttpStatusCode.Forbidden, "FCMS2001"),
            ("Failed", "me/sessions" or "me/two-factor" or "me/logins" or "me/preferences") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            ("ThisTabEnded", "me/sessions") when ended => Problem(HttpStatusCode.Unauthorized, "FCMS3102"),
            ("ThisTabEnded", _) when method == HttpMethod.Delete => End(),
            (_, "me/two-factor/enrol") => Json(new JsonObject { ["sharedKey"] = "2345672345672345", ["authenticatorUri"] = "otpauth://totp/x?secret=2345672345672345" }),
            ("WrongCode", "me/two-factor/verify") => Problem(HttpStatusCode.UnprocessableEntity, "FCMS3063", "code"),
            (_, "me/two-factor/verify") => Json(new JsonObject { ["recoveryCodes"] = new JsonArray("12345-67890", "23456-78901") }),
            _ => null,
        };
        if (state is "On" or "Disable")
        {
            Management.TwoFactor["enabled"] = true;
            Management.TwoFactor["recoveryCodesLeft"] = 2;
        }

        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo(AccountPage.Route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll("section.cms-card").Count.ShouldBe(5));
        app.WaitForAssertion(() => app.FindAll("section.cms-card [role=status]").ShouldBeEmpty());

        switch (state)
        {
            case "Enrol" or "WrongCode" or "Codes":
                Click(app, 1, 0);
                app.WaitForAssertion(() => app.Find("svg.qr-code"));
                if (state != "Enrol")
                {
                    app.Find("section.cms-card input").Input("123456");
                    Click(app, 1, 0);
                    app.WaitForAssertion(() => app.Find(state == "Codes" ? "pre.account-codes" : "input[aria-invalid=true]"));
                }

                break;
            case "Disable":
                Click(app, 1, 1);
                app.WaitForAssertion(() => app.Find("section.cms-card input"));
                break;
            case "ThisTabEnded":
                Click(app, 0, 0);
                app.WaitForAssertion(() => app.FindAll("section.cms-card")[0].QuerySelector("[role=status]").ShouldNotBeNull());
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
