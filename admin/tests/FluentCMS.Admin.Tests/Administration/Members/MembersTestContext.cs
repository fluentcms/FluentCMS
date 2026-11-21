using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Members;

// The members area against a fake of /members, /member-groups and /roles that keeps what the pages change, so that a test sees
// the answers a page reads after its change. A refusal is given with Management.Answer, which is asked first.
public abstract class MembersTestContext : AdministrationTestContext
{
    internal static readonly Guid Reader = new("0193a1b2-0000-7000-8000-0000000000a1");
    internal static readonly Guid Author = new("0193a1b2-0000-7000-8000-0000000000a2");
    internal static readonly Guid Ada = new("0193a1b2-0000-7000-8000-0000000000d1");
    internal static readonly Guid Grace = new("0193a1b2-0000-7000-8000-0000000000d2");
    internal static readonly Guid Readers = new("0193a1b2-0000-7000-8000-0000000000f1");
    internal static readonly Guid Writers = new("0193a1b2-0000-7000-8000-0000000000f2");
    internal static readonly Guid Laptop = new("0193a1b2-0000-7000-8000-0000000000e1");
    internal const string Created = "2026-11-02T08:00:00+00:00";

    private int created;

    protected MembersTestContext()
    {
        Roles.Add(Role(Reader, null, "Reader"));
        Roles.Add(Role(Author, MainSite, "Author"));
        Groups.Add(Group(Readers, "readers", Reader, "Reader", null));
        Groups.Add(Group(Writers, "writers", Author, "Author", MainSite));
        Members.Add(Member(Ada, "ada@example.com", "Ada", verified: true, [Readers]));
        Members.Add(Member(Grace, "grace@example.com", null, verified: false, []));
        Sessions[Ada] = [new JsonObject { ["id"] = Laptop, ["clientId"] = "shop", ["clientName"] = "Shop", ["createdAt"] = Created, ["scopes"] = new JsonArray() }];
        Logins[Ada] = [new JsonObject { ["provider"] = "GitHub", ["displayName"] = "GitHub" }];
        Management.Respond = Answer;
    }

    internal JsonArray Roles { get; } = [];

    internal JsonArray Groups { get; } = [];

    internal JsonArray Members { get; } = [];

    internal Dictionary<Guid, JsonArray> Sessions { get; } = [];

    internal Dictionary<Guid, JsonArray> Logins { get; } = [];

    // The query strings of the reads of the list.
    internal List<string> MemberQueries { get; } = [];

    internal static JsonObject Role(Guid id, Guid? site, string name) =>
        new() { ["id"] = id, ["siteId"] = site, ["name"] = name, ["description"] = null, ["builtIn"] = false, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() };

    internal static JsonObject Group(Guid id, string name, Guid role, string roleName, Guid? roleSite, string? description = null) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["description"] = description,
        ["roleId"] = role,
        ["roleName"] = roleName,
        ["roleSiteId"] = roleSite,
        ["members"] = 0,
        ["createdAt"] = Created,
    };

    internal JsonObject Member(Guid id, string email, string? name, bool verified, Guid[] groups) => new()
    {
        ["id"] = id,
        ["email"] = email,
        ["emailVerified"] = verified,
        ["displayName"] = name,
        ["createdAt"] = Created,
        ["lastLoginAt"] = null,
        ["deactivatedAt"] = null,
        ["lockoutEnd"] = null,
        ["groups"] = new JsonArray([.. groups.Select(group => (JsonNode)GroupRef(group))]),
    };

    internal JsonObject MemberOf(Guid id) => Members.Single(member => (Guid)member!["id"]! == id)!.AsObject();

    internal JsonObject GroupOf(Guid id) => Groups.Single(group => (Guid)group!["id"]! == id)!.AsObject();

    // The last call of the method to the path, with its body.
    internal string? BodyOf(HttpMethod method, string path) => Management.Calls.Last(call => call.Method == method && call.Path == path).Body;

    internal static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text: the visible text and the hidden part.
    internal static IElement Button(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    internal static string[] Buttons(IElement scope) => [.. scope.QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim())];

    internal static string Message(IRenderedComponent<AdminApp> app, string heading) =>
        Section(app, heading).QuerySelector(".members-message")!.TextContent;

    // The texts of the items of the section's first list, without their buttons.
    internal static string[] Items(IElement section) =>
        [.. section.QuerySelectorAll("li").Select(item => string.Join(' ', item.ChildNodes.Where(node => node is not IElement { LocalName: "fluent-button" or "div" }).Select(node => node.TextContent.Trim()).Where(text => text.Length > 0)))];

    // The last FocusAsync of the page went to the element the component holds in the field.
    internal void ShouldBeFocused<TComponent>(IRenderedComponent<AdminApp> app, string field)
        where TComponent : IComponent
    {
        var component = app.FindComponent<TComponent>().Instance;
        var expected = (ElementReference)typeof(TComponent).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        var focused = (ElementReference)JSInterop.Invocations.Last(call => call.Identifier == "Blazor._internal.domWrapper.focus").Arguments[0]!;
        focused.Id.ShouldNotBeNullOrEmpty();
        focused.Id.ShouldBe(expected.Id);
    }

    private JsonObject GroupRef(Guid group) => new() { ["id"] = group, ["name"] = GroupOf(group)["name"]!.DeepClone() };

    private JsonArray GroupsWithCounts()
    {
        foreach (var group in Groups)
        {
            group!["members"] = Members.Count(member => member!["groups"]!.AsArray().Any(held => (Guid)held!["id"]! == (Guid)group["id"]!));
        }

        return (JsonArray)Groups.DeepClone();
    }

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body)
    {
        var method = request.Method;
        var segments = path.Split('/');
        if (path == "roles" && method == HttpMethod.Get)
        {
            return Json(new JsonObject { ["items"] = Roles.DeepClone() });
        }

        if (path == "member-groups")
        {
            if (method == HttpMethod.Get)
            {
                return Json(new JsonObject { ["items"] = GroupsWithCounts() });
            }

            var sent = JsonNode.Parse(body!)!;
            var role = Roles.Single(item => (Guid)item!["id"]! == Guid.Parse((string)sent["roleId"]!))!;
            var group = Group(new Guid($"0193a1b2-0000-7000-8000-{++created:D12}"), (string)sent["name"]!, (Guid)role["id"]!, (string)role["name"]!, (Guid?)role["siteId"], (string?)sent["description"]);
            Groups.Add(group);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = Json(group.DeepClone()).Content };
        }

        if (segments is ["member-groups", var groupId] && Guid.TryParse(groupId, out var target))
        {
            if (Groups.FirstOrDefault(item => (Guid)item!["id"]! == target) is not JsonObject group)
            {
                return Problem(HttpStatusCode.NotFound, "FCMS3168", "groupId");
            }

            if (method == HttpMethod.Get)
            {
                return Json(group.DeepClone());
            }

            if (method == HttpMethod.Delete)
            {
                Groups.Remove(group);
                foreach (var member in Members)
                {
                    var held = member!["groups"]!.AsArray();
                    if (held.FirstOrDefault(item => (Guid)item!["id"]! == target) is { } membership)
                    {
                        held.Remove(membership);
                    }
                }

                return NoContent();
            }

            var sent = JsonNode.Parse(body!)!;
            var role = Roles.Single(item => (Guid)item!["id"]! == Guid.Parse((string)sent["roleId"]!))!;
            (group["name"], group["description"], group["roleId"], group["roleName"], group["roleSiteId"]) =
                (sent["name"]!.DeepClone(), sent["description"]?.DeepClone(), role["id"]!.DeepClone(), role["name"]!.DeepClone(), role["siteId"]?.DeepClone());
            return Json(group.DeepClone());
        }

        if (path == "members" && method == HttpMethod.Get)
        {
            var query = request.RequestUri!.Query.TrimStart('?');
            MemberQueries.Add(query);
            string? Value(string name) => query.Split('&').FirstOrDefault(pair => pair.StartsWith(name + "=", StringComparison.Ordinal)) is { } pair ? Uri.UnescapeDataString(pair[(name.Length + 1)..]) : null;
            var (search, group, verified, active) = (Value("search"), Value("group"), Value("verified"), Value("active"));
            var matching = Members
                .Where(member => search is null || ((string)member!["email"]!).Contains(search, StringComparison.OrdinalIgnoreCase))
                .Where(member => group is null || member!["groups"]!.AsArray().Any(held => (Guid)held!["id"]! == Guid.Parse(group)))
                .Where(member => verified is null || (bool)member!["emailVerified"]! == (verified == "true"))
                .Where(member => active is null || (member!["deactivatedAt"] is null) == (active == "true"))
                .Select(member => member!.DeepClone())
                .ToList();
            return Json(new JsonObject { ["items"] = new JsonArray([.. matching]), ["total"] = matching.Count, ["page"] = 1, ["pageSize"] = 25 });
        }

        if (path == "members" && method == HttpMethod.Post)
        {
            var sent = JsonNode.Parse(body!)!;
            var member = Member(new Guid($"0193a1b2-0000-7000-8000-{++created:D12}"), (string)sent["email"]!, (string?)sent["displayName"], verified: false, []);
            Members.Add(member);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = Json(member.DeepClone()).Content };
        }

        if (segments is not ["members", var memberId, ..] || !Guid.TryParse(memberId, out var id))
        {
            return null;
        }

        if (Members.FirstOrDefault(member => (Guid)member!["id"]! == id) is not JsonObject found)
        {
            return Problem(HttpStatusCode.NotFound, "FCMS3161", "id");
        }

        switch (segments[2..])
        {
            case [] when method == HttpMethod.Get:
                break;
            case [] when method == HttpMethod.Patch:
                var name = (string?)JsonNode.Parse(body!)!["displayName"];
                found["displayName"] = string.IsNullOrWhiteSpace(name) ? null : name;
                break;
            case [] when method == HttpMethod.Delete:
                Members.Remove(found);
                return NoContent();
            case ["deactivate"]:
                found["deactivatedAt"] = "2026-11-20T10:00:00+00:00";
                EndSessions(id);
                break;
            case ["reactivate"]:
                found["deactivatedAt"] = null;
                break;
            case ["groups", var group] when method == HttpMethod.Put:
                found["groups"]!.AsArray().Add(GroupRef(Guid.Parse(group)));
                break;
            case ["groups", var group] when method == HttpMethod.Delete:
                found["groups"]!.AsArray().Remove(found["groups"]!.AsArray().Single(held => (Guid)held!["id"]! == Guid.Parse(group)));
                break;
            case ["sessions"]:
                return Json(new JsonObject { ["items"] = Sessions.GetValueOrDefault(id)?.DeepClone() ?? new JsonArray() });
            case ["sessions", var session]:
                return Remove(Sessions[id], Sessions[id].Single(item => (Guid)item!["id"]! == Guid.Parse(session))!);
            case ["logins"]:
                return Json(new JsonObject { ["items"] = Logins.GetValueOrDefault(id)?.DeepClone() ?? new JsonArray() });
            case ["logins", var provider]:
                EndSessions(id);
                return Remove(Logins[id], Logins[id].Single(item => (string)item!["provider"]! == provider)!);
            default:
                return null;
        }

        return Json(found.DeepClone());
    }

    // As the server, where deactivate and the removal of a login end every session of the member, and reactivate none.
    private void EndSessions(Guid member) => Sessions.GetValueOrDefault(member)?.Clear();

    private static HttpResponseMessage Remove(JsonArray items, JsonNode item)
    {
        items.Remove(item);
        return NoContent();
    }
}
