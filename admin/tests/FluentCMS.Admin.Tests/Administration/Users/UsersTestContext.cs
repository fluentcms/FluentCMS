using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Users;

// The users area against a fake of /users, /invitations and /roles that keeps what the pages change, so that a test sees the
// answers a page reads after its change. A refusal is given with Management.Answer, which is asked first.
public abstract class UsersTestContext : AdministrationTestContext
{
    internal static readonly Guid SuperAdmin = new("00000000-0000-7000-8000-000000000001");
    internal static readonly Guid Editor = new("00000000-0000-7000-8000-000000000002");
    internal static readonly Guid Author = new("0193a1b2-0000-7000-8000-0000000000a1");
    internal static readonly Guid Ada = new("0193a1b2-0000-7000-8000-0000000000d1");
    internal static readonly Guid Grace = new("0193a1b2-0000-7000-8000-0000000000d2");
    internal static readonly Guid Laptop = new("0193a1b2-0000-7000-8000-0000000000e1");
    internal const string Created = "2026-11-02T08:00:00+00:00";

    private int invitationNumber;

    protected UsersTestContext()
    {
        Roles.Add(Role(SuperAdmin, null, "Super Admin"));
        Roles.Add(Role(Editor, null, "Editor"));
        Roles.Add(Role(Author, MainSite, "Author"));
        Users.Add(User(Ada, "ada", [SuperAdmin], twoFactor: true));
        Users.Add(User(Grace, "grace", [Editor, Author]));
        Sessions[Grace] = [new JsonObject { ["id"] = Laptop, ["clientId"] = "fluentcms-admin", ["clientName"] = "FluentCMS admin", ["createdAt"] = Created, ["scopes"] = new JsonArray() }];
        Logins[Grace] = [new JsonObject { ["provider"] = "GitHub", ["providerKey"] = "42", ["displayName"] = "GitHub" }];
        Management.Respond = Answer;
    }

    internal JsonArray Roles { get; } = [];

    internal JsonArray Users { get; } = [];

    internal JsonArray Invitations { get; } = [];

    internal Dictionary<Guid, JsonArray> Sessions { get; } = [];

    internal Dictionary<Guid, JsonArray> Logins { get; } = [];

    // The query strings of the reads of the list.
    internal List<string> UserQueries { get; } = [];

    internal static JsonObject Role(Guid id, Guid? site, string name) =>
        new() { ["id"] = id, ["siteId"] = site, ["name"] = name, ["description"] = null, ["builtIn"] = false, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() };

    internal JsonObject User(Guid id, string name, Guid[] roles, bool twoFactor = false) => new()
    {
        ["id"] = id,
        ["userName"] = name,
        ["email"] = $"{name}@example.com",
        ["emailConfirmed"] = true,
        ["createdAt"] = Created,
        ["deactivatedAt"] = null,
        ["mustChangePassword"] = false,
        ["twoFactorEnabled"] = twoFactor,
        ["lockoutEnd"] = null,
        ["roles"] = new JsonArray([.. roles.Select(role => (JsonNode)RoleOfUser(role))]),
    };

    internal JsonObject UserOf(Guid id) => Users.Single(user => (Guid)user!["id"]! == id)!.AsObject();

    // The last call of the method to the path, with its body.
    internal string? BodyOf(HttpMethod method, string path) => Management.Calls.Last(call => call.Method == method && call.Path == path).Body;

    internal static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text: the visible text and the hidden part.
    internal static IElement Button(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    internal static string[] Buttons(IElement scope) => [.. scope.QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim())];

    internal static string Message(IRenderedComponent<AdminApp> app, string heading) =>
        Section(app, heading).QuerySelector(".users-message")!.TextContent;

    // The texts of the items of the section's first list.
    internal static string[] Items(IElement section) =>
        [.. section.QuerySelectorAll("li").Select(item => string.Join(' ', item.ChildNodes.Where(node => node is not IElement { LocalName: "fluent-button" }).Select(node => node.TextContent.Trim()).Where(text => text.Length > 0)))];

    // The last FocusAsync of the page went to the element the component holds in the field. bUnit leaves the reference of an
    // element inside a card's child content out of the markup, so the reference is read from the component.
    internal void ShouldBeFocused<TComponent>(IRenderedComponent<AdminApp> app, string field)
        where TComponent : IComponent
    {
        var component = app.FindComponent<TComponent>().Instance;
        var expected = (ElementReference)typeof(TComponent).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        var focused = (ElementReference)JSInterop.Invocations.Last(call => call.Identifier == "Blazor._internal.domWrapper.focus").Arguments[0]!;
        focused.Id.ShouldNotBeNullOrEmpty();
        focused.Id.ShouldBe(expected.Id);
    }

    private JsonObject RoleOfUser(Guid role)
    {
        var found = Roles.Single(item => (Guid)item!["id"]! == role)!;
        return new JsonObject { ["id"] = role, ["name"] = found["name"]!.DeepClone(), ["siteId"] = found["siteId"]?.DeepClone() };
    }

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body)
    {
        var method = request.Method;
        var segments = path.Split('/');
        if (path == "roles" && method == HttpMethod.Get)
        {
            return Json(new JsonObject { ["items"] = Roles.DeepClone() });
        }

        if (path == "users" && method == HttpMethod.Get)
        {
            var query = request.RequestUri!.Query.TrimStart('?');
            UserQueries.Add(query);
            var search = query.Split('&').FirstOrDefault(pair => pair.StartsWith("search=", StringComparison.Ordinal))?["search=".Length..];
            var found = Users.Where(user => search is null || ((string)user!["userName"]!).Contains(Uri.UnescapeDataString(search), StringComparison.OrdinalIgnoreCase)).Select(user => user!.DeepClone()).ToList();
            return Json(new JsonObject { ["items"] = new JsonArray([.. found]), ["total"] = found.Count, ["page"] = 1, ["pageSize"] = 25 });
        }

        if (path == "invitations")
        {
            if (method == HttpMethod.Get)
            {
                return Json(new JsonObject { ["items"] = Invitations.DeepClone() });
            }

            var sent = JsonNode.Parse(body!)!;
            var id = new Guid($"0193a1b2-0000-7000-8000-{++invitationNumber:D12}");
            Invitations.Add(new JsonObject
            {
                ["id"] = id,
                ["email"] = sent["email"]!.DeepClone(),
                ["roleIds"] = sent["roleIds"]!.DeepClone(),
                ["createdAt"] = Created,
                ["createdBy"] = Ada,
                ["expiresAt"] = "2026-11-09T08:00:00+00:00",
            });
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = Json(new JsonObject { ["id"] = id, ["email"] = sent["email"]!.DeepClone(), ["expiresAt"] = "2026-11-09T08:00:00+00:00" }).Content,
            };
        }

        if (segments is ["invitations", var invitation] && method == HttpMethod.Delete)
        {
            var removed = Invitations.FirstOrDefault(item => (Guid)item!["id"]! == Guid.Parse(invitation));
            return removed is null ? Problem(HttpStatusCode.NotFound, "FCMS3048", "id") : Remove(Invitations, removed);
        }

        if (segments is not ["users", var userId, ..] || !Guid.TryParse(userId, out var target))
        {
            return null;
        }

        if (Users.FirstOrDefault(user => (Guid)user!["id"]! == target) is not JsonObject user)
        {
            return Problem(HttpStatusCode.NotFound, "FCMS3046", "id");
        }

        switch (segments[2..])
        {
            case [] when method == HttpMethod.Get:
                break;
            case [] when method == HttpMethod.Patch:
                user["mustChangePassword"] = JsonNode.Parse(body!)!["mustChangePassword"]!.GetValue<bool>();
                break;
            case ["deactivate"]:
                user["deactivatedAt"] = "2026-11-20T10:00:00+00:00";
                break;
            case ["reactivate"]:
                user["deactivatedAt"] = null;
                break;
            case ["two-factor", "reset"]:
                user["twoFactorEnabled"] = false;
                break;
            case ["roles", var role] when method == HttpMethod.Put:
                user["roles"]!.AsArray().Add(RoleOfUser(Guid.Parse(role)));
                break;
            case ["roles", var role] when method == HttpMethod.Delete:
                user["roles"]!.AsArray().Remove(user["roles"]!.AsArray().Single(held => (Guid)held!["id"]! == Guid.Parse(role)));
                break;
            case ["sessions"]:
                return Json(new JsonObject { ["items"] = Sessions.GetValueOrDefault(target)?.DeepClone() ?? new JsonArray() });
            case ["sessions", var session]:
                return Remove(Sessions[target], Sessions[target].Single(item => (Guid)item!["id"]! == Guid.Parse(session))!);
            case ["logins"]:
                return Json(new JsonObject { ["items"] = Logins.GetValueOrDefault(target)?.DeepClone() ?? new JsonArray() });
            case ["logins", var provider]:
                return Remove(Logins[target], Logins[target].Single(item => (string)item!["provider"]! == provider)!);
            default:
                return null;
        }

        return Json(user.DeepClone());
    }

    private static HttpResponseMessage Remove(JsonArray items, JsonNode item)
    {
        items.Remove(item);
        return NoContent();
    }
}
