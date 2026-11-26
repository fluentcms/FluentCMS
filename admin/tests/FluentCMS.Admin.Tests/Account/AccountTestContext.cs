using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Administration.Account;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Tests.Administration;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Admin.Tests.Account;

// The account page (A-09) against the fake Management API, with the resources as they ship. It is a page of the
// administration assembly, which the shell loads on its first visit (A-41).
public abstract class AccountTestContext : AdminTestContext
{
    protected AccountTestContext() => UseNeutralResources();

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    // The loader of the administration assembly, which the page is in.
    internal AdministrationTestContext.AdministrationLoader Loader { get; } = new();

    // The routes the page asked the session to sign in again to come back to.
    protected List<string?> SignIns => ((SignedInTestSession)Services.GetRequiredService<IAdminSession>()).SignIns;

    // The routes the page asked the session to sign in again to on the login page, to come back to.
    protected List<string?> Reauthentications => ((SignedInTestSession)Services.GetRequiredService<IAdminSession>()).Reauthentications;

    // The addresses the page asked the session to leave for to change the password.
    protected List<string> PasswordChanges => ((SignedInTestSession)Services.GetRequiredService<IAdminSession>()).PasswordChanges;

    // The authorization server cannot be reached when the page signs in again.
    protected void FailSignIn(Exception? failure = null) =>
        ((SignedInTestSession)Services.GetRequiredService<IAdminSession>()).SignInError = failure ?? new HttpRequestException("The authorization server did not answer.");

    protected static List<string> Names(IElement section) =>
        [.. section.QuerySelectorAll(".account-item-name").Select(name => name.TextContent.Trim())];

    protected static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text: the visible text and the hidden part.
    protected static IElement Button(IElement section, string name) =>
        section.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    protected static string Message(IRenderedComponent<AdminApp> app, string heading) =>
        Section(app, heading).QuerySelector(".account-message")!.TextContent;

    protected static JsonObject Status(bool enabled, int left, bool required = false, bool pending = false) => new()
    {
        ["enabled"] = enabled,
        ["required"] = required,
        ["pending"] = pending,
        ["recoveryCodesLeft"] = left,
    };

    // The last FocusAsync of the page went to the element the section holds in the field. bUnit leaves the element reference
    // of an element rendered inside a card's child content out of the markup, so the reference is read from the section.
    protected void ShouldBeFocused<TSection>(IRenderedComponent<AdminApp> app, string field)
        where TSection : IComponent
    {
        var section = app.FindComponent<TSection>().Instance;
        var expected = (ElementReference)typeof(TSection).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(section)!;
        var focused = (ElementReference)JSInterop.Invocations.Last(call => call.Identifier == "Blazor._internal.domWrapper.focus").Arguments[0]!;
        focused.Id.ShouldNotBeNullOrEmpty();
        focused.Id.ShouldBe(expected.Id);
    }

    protected IRenderedComponent<AdminApp> Load(string route = AccountPage.Route, string configuration = ValidConfiguration)
    {
        Services.AddScoped<IAdminAssemblyLoader>(_ => Loader);
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
