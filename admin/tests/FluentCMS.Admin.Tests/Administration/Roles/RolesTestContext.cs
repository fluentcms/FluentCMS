using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Shouldly;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// The roles of the fake Management API, the catalogue of GET /permissions and the media plugin that contributes media.read.
// The caller holds roles.manage for the instance with the permissions of the catalogue but users.manage, and reads and
// writes articles in the main site. A test changes the roles, the grant, or what a write answers with Refuse.
public abstract class RolesTestContext : AdministrationTestContext
{
    internal static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");
    internal static readonly Guid SuperAdmin = new("00000000-0000-7000-8000-000000000001");
    internal static readonly Guid Author = new("00000000-0000-7000-8000-000000000003");
    internal static readonly Guid SeededPublic = new("00000000-0000-7000-8000-000000000004");
    internal static readonly Guid Reviewers = new("0193a1b2-0000-7000-8000-0000000000b1");
    internal static readonly Guid MainPublic = new("0193a1b2-0000-7000-8000-0000000000b2");
    internal static readonly Guid ShopEditors = new("0193a1b2-0000-7000-8000-0000000000b3");

    internal static readonly string[] Held = ["roles.manage", "schema.read", "media.read", "settings.media.manage"];

    private int created;

    protected RolesTestContext()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(MainSite, Type("article"));
        var media = Plugin("media", enabled: true);
        media["contributions"]!["permissions"] = new JsonArray("media.read");
        Management.Plugins[MainSite].Add(media);
        Management.Respond = (request, path, body) => Serve(request.Method, path, body);
    }

    // The roles as GET /roles answers them, each with its grants.
    internal JsonArray Roles { get; } =
    [
        Role(SuperAdmin, null, "Super Admin", builtIn: true, Grant("*")),
        Role(Author, null, "Author", builtIn: true, Grant("content.*.create"), Grant("content.*.read", new JsonObject { ["rowRule"] = "own-entries" })),
        Role(SeededPublic, null, "Public", builtIn: true),
        Role(Reviewers, MainSite, "Reviewers", builtIn: false,
            Grant("schema.read"),
            Grant("content.article.read", new JsonObject
            {
                ["sites"] = new JsonArray(MainSite.ToString("D")),
                ["locales"] = new JsonArray("en", "fr"),
                ["fields"] = new JsonArray("title", "body"),
                ["rowRule"] = "own-entries",
                ["resources"] = new JsonArray("folder-1", "folder-2"),
            }),
            Grant("media.read", new JsonObject { ["resources"] = new JsonArray("0193a1b2-0000-7000-8000-0000000000f1") })),
        Role(MainPublic, MainSite, "Public", builtIn: true),
        Role(ShopEditors, Shop, "Shop editors", builtIn: false, Grant("content.*.read")),
    ];

    internal JsonArray Permissions { get; } =
    [
        Permission("users.manage", "Invite, edit and deactivate users.", "core"),
        Permission("roles.manage", "Create and edit roles and their grants.", "core"),
        Permission("schema.read", "Read the content schema.", "core"),
        Permission("content.article.create", "Create article entries.", "content"),
        Permission("content.article.read", "Read article entries.", "content"),
        Permission("content.article.restore", "Restore article entries from the trash.", "content"),
        Permission("media.read", "Read media.", "plugin"),
        Permission("settings.media.manage", "Change the media settings.", "settings"),
    ];

    // The body of every write, by method.
    internal List<(HttpMethod Method, string Path, JsonObject? Body)> Writes { get; } = [];

    // Answers a write in place of the fake's role store when it returns one.
    internal Func<HttpMethod, string, HttpResponseMessage?>? Refuse { get; set; }

    internal static JsonObject Role(Guid id, Guid? site, string name, bool builtIn, params JsonObject[] grants) => new()
    {
        ["id"] = id,
        ["siteId"] = site,
        ["name"] = name,
        ["description"] = null,
        ["builtIn"] = builtIn,
        ["requireTwoFactor"] = false,
        ["grants"] = new JsonArray([.. grants]),
    };

    internal static JsonObject Grant(string permission, JsonObject? constraints = null) => new() { ["permission"] = permission, ["constraints"] = constraints };

    internal static JsonObject Permission(string name, string description, string source) => new() { ["name"] = name, ["description"] = description, ["source"] = source };

    internal JsonObject RoleOf(Guid id) => Roles.Single(role => (Guid)role!["id"]! == id)!.AsObject();

    // The caller with roles.manage for the instance, as the area's gate lets it in, with every other permission of Held.
    internal IRenderedComponent<AdminApp> Open(string route, string[]? inSite = null, string[]? forInstance = null)
    {
        Grant(MainSite, inSite ?? [], forInstance ?? Held);
        return Load(route);
    }

    internal static IElement Field(IRenderedComponent<AdminApp> app, string label)
    {
        var found = app.FindAll("label").Single(item => item.TextContent.Replace("*", string.Empty, StringComparison.Ordinal).Trim() == label);
        return app.Find($"#{found.GetAttribute("for")}");
    }

    internal static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    internal static string[] Options(IElement select) => [.. select.QuerySelectorAll("option").Select(option => option.TextContent.Trim())];

    internal static void Save(IRenderedComponent<AdminApp> app) => app.Find("main form").Submit();

    internal static string Alert(IRenderedComponent<AdminApp> app) => app.Find("main form > [role=alert]").TextContent.Trim();

    // Adds a grant through the editor's pick of a source and a permission.
    internal static void Add(IRenderedComponent<AdminApp> app, string source, string permission)
    {
        Field(app, "Kind of permission").Change(source);
        app.WaitForAssertion(() => Options(Field(app, "Permission or pattern")).ShouldContain(text => text.StartsWith(permission + ":", StringComparison.Ordinal)));
        Field(app, "Permission or pattern").Change(permission);
        Button(app, "Add the grant").Click();
    }

    internal static string[] GrantsShown(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main [data-grant]").Select(item => item.GetAttribute("data-grant")!)];

    internal JsonObject LastWrite(HttpMethod method) => Writes.Last(write => write.Method == method).Body!;

    private HttpResponseMessage? Serve(HttpMethod method, string path, string? body)
    {
        var segments = path.Split('/');
        if (path == "permissions")
        {
            return Json(new JsonObject { ["items"] = Permissions.DeepClone() });
        }

        if (segments[0] != "roles")
        {
            return null;
        }

        if (method != HttpMethod.Get)
        {
            Writes.Add((method, path, body is null ? null : JsonNode.Parse(body)!.AsObject()));
            if (Refuse?.Invoke(method, path) is { } refused)
            {
                return refused;
            }
        }

        switch (segments)
        {
            case ["roles"] when method == HttpMethod.Get:
                return Json(new JsonObject { ["items"] = Roles.DeepClone() });
            case ["roles"] when method == HttpMethod.Post:
                var sent = JsonNode.Parse(body!)!.AsObject();
                var role = Role(new Guid($"0193a1b2-0000-7000-8000-{++created:D12}"), sent["siteId"] is { } site ? Guid.Parse((string)site!) : null, (string)sent["name"]!, builtIn: false);
                role["description"] = sent["description"]?.DeepClone();
                role["requireTwoFactor"] = sent["requireTwoFactor"]?.DeepClone() ?? false;
                role["grants"] = Normalized(sent["grants"]!.AsArray());
                Roles.Add(role);
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = Json(role.DeepClone()).Content };
        }

        if (segments is not ["roles", var id] || !Guid.TryParse(id, out var roleId) || Roles.FirstOrDefault(item => (Guid)item!["id"]! == roleId) is not JsonObject found)
        {
            return Problem(HttpStatusCode.NotFound, "FCMS3040", "id");
        }

        if (method == HttpMethod.Put)
        {
            var change = JsonNode.Parse(body!)!.AsObject();
            found["name"] = change["name"]!.DeepClone();
            found["description"] = change["description"]?.DeepClone();
            found["requireTwoFactor"] = change["requireTwoFactor"]?.DeepClone() ?? found["requireTwoFactor"]!.DeepClone();
            found["grants"] = Normalized(change["grants"]!.AsArray());
        }
        else if (method == HttpMethod.Delete)
        {
            Roles.Remove(found);
            return NoContent();
        }

        return Json(found.DeepClone());
    }

    // As the server answers a grant: null constraints for none.
    private static JsonArray Normalized(JsonArray grants) =>
        [.. grants.Select(grant => (JsonNode)new JsonObject { ["permission"] = grant!["permission"]!.DeepClone(), ["constraints"] = grant["constraints"]?.DeepClone() })];
}
