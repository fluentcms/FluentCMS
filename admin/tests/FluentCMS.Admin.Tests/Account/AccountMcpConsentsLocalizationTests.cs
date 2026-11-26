using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Account;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Tests.Administration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The consents to MCP clients of the account page in each state that shows texts of its own, under both pseudo-cultures. The
// client's name is marked as a resource is, and the dates are in a culture whose dates have no word.
public sealed class AccountMcpConsentsLocalizationTests : AdminTestContext
{
    private static readonly string[] StateNames = ["Lists", "Undated", "Empty", "Failed", "Asked", "Ended", "Gone", "Changed", "Busy", "EndFailed"];

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
    public void EveryTextOfTheConsentsComesFromTheResources(string culture, string state)
    {
        UseInterfaceCulture(culture);
        var name = PseudoCulture.Transform("Agent", culture == PseudoCulture.RightToLeft);
        JsonArray items = state == "Empty" ? [] :
        [
            Consent(name, state == "Undated" ? null : "2026-11-16T09:30:00+00:00", active: true),
            Consent(name, state == "Undated" ? null : "2026-11-15T18:05:00+00:00", active: false),
        ];
        Management.Plugins[MainSite].Add(Plugin(AccountConsentsApi.Plugin, enabled: true));
        Management.Preferences["dateFormat"] = "de-DE";
        Management.Answer = (method, path) => (state, method.Method, path) switch
        {
            ("Failed", "GET", "mcp/consents") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            (_, "GET", "mcp/consents") => Json(new JsonObject { ["items"] = items.DeepClone() }),
            ("Gone", "DELETE", _) => Problem(HttpStatusCode.NotFound, "FCMS1603"),
            ("Changed", "DELETE", _) => Problem(HttpStatusCode.Conflict, "FCMS1609"),
            ("Busy", "DELETE", _) => Problem(HttpStatusCode.ServiceUnavailable, "FCMS3106"),
            ("EndFailed", "DELETE", _) => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            (_, "DELETE", _) => NoContent(),
            _ => null,
        };
        Services.AddScoped<IAdminAssemblyLoader>(_ => new AdministrationTestContext.AdministrationLoader());
        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo(AccountPage.Route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll("section.cms-card").Count.ShouldBe(6));
        app.WaitForAssertion(() => app.FindAll("section.cms-card [role=status]").ShouldBeEmpty());

        if (state is "Asked" or "Ended" or "Gone" or "Changed" or "Busy" or "EndFailed")
        {
            var section = app.FindComponent<AccountMcpConsents>();
            section.FindAll("ul fluent-button")[0].Click();
            app.WaitForAssertion(() => section.FindComponent<CmsDialog>().Instance.Open.ShouldBeTrue());
            if (state != "Asked")
            {
                // The dialog's actions come last: the confirmation, then Keep it.
                section.FindAll("fluent-dialog fluent-button")[^2].Click();
                app.WaitForAssertion(() => section.Find(".account-message:not(:empty)"));
            }
        }

        var theme = app.FindAll("section.cms-card select")[0];
        foreach (var option in app.FindAll("section.cms-card select option[value]:not([value=''])").Where(option => option.ParentElement != theme))
        {
            option.Remove();
        }

        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    private static JsonObject Consent(string name, string? consentedAt, bool active) => new()
    {
        ["id"] = Guid.NewGuid(),
        ["clientId"] = "1234",
        ["name"] = name,
        ["redirectUris"] = new JsonArray(),
        ["registeredAt"] = "2026-11-10T08:00:00+00:00",
        ["consentedAt"] = consentedAt,
        ["active"] = active,
    };
}
