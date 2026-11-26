using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.MultiSite.Admin.Tests;

// The half's pages with the admin's services they inject: the sites and host names of the instance in memory behind
// IManagementApi, the shell's context, which counts its reads of the sites, and an announcer that keeps what it was given.
public abstract class MultiSiteTestContext : BunitContext
{
    internal static readonly Guid Main = new("0193a1b2-0000-7000-8000-00000000a001");
    internal static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    protected MultiSiteTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Server);
        Services.AddSingleton<IAdminContext>(Admin);
        Services.AddSingleton<IAnnouncer>(Announcer);
        Server.AddSite(Main, "Main", "main", isDefault: true);
        Server.AddSite(Shop, "Shop", "shop");
        Admin.Site = FakeAdminContext.SiteOf(Main);
    }

    internal FakeSites Server { get; } = new();

    internal FakeAdminContext Admin { get; } = new();

    internal FakeAnnouncer Announcer { get; } = new();

    protected static IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    protected static bool HasButton<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").Any(button => button.TextContent.Trim() == text);

    protected static IElement Field<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent
    {
        var found = cut.FindAll("label").First(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label);
        return cut.Find("#" + found.GetAttribute("for"));
    }

    // The problem the server answers, with its code on the member of the request it is about.
    internal static ApiProblemException Problem(int status, string code, string? path = null) =>
        new(new ApiProblem { Status = status, Code = code, Errors = path is null ? [] : [new ApiProblemError { Path = path, Code = code, Message = "Refused." }] });
}

internal sealed class FakeAnnouncer : IAnnouncer
{
    public List<string> Messages { get; } = [];

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add(message);
}

// The shell's context: the site it shows, and its reads of the user's sites, after which it shows what Moves gives, as the
// shell moves to the default site when the one it showed is gone.
internal sealed class FakeAdminContext : IAdminContext, IAdminAccess
{
    public MeResponse? User => null;

    public MySiteInfo? Site { get; set; }

    public string? ContentLocale => "en";

    public IAdminAccess Access => this;

    public int SiteReads { get; private set; }

    // The site each read was told is gone, null for none.
    public List<Guid?> Gone { get; } = [];

    public Func<MySiteInfo?>? Moves { get; set; }

    public event Action? Changed;

    public bool Allows(string permission) => true;

    public bool AllowsForInstance(string permission) => true;

    public bool AllowsForSomeResources(string permission) => Allows(permission);

    public TypeAccessInfo? ForType(string type) => null;

    public Task ReadSitesAsync(Guid? gone = null)
    {
        SiteReads++;
        Gone.Add(gone);
        if (Moves is { } move)
        {
            Site = move();
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public static MySiteInfo SiteOf(Guid id) => new()
    {
        Id = id,
        Name = id == MultiSiteTestContext.Main ? "Main" : "Shop",
        Slug = id == MultiSiteTestContext.Main ? "main" : "shop",
        DefaultLocale = "en",
        IsDefault = id == MultiSiteTestContext.Main,
        Locales = [new MySiteLocale { Code = "en", Position = 0, Enabled = true }],
    };
}

// GET and POST /sites, GET and DELETE /sites/{id} and the host names of a site, as the server answers them, over sites and
// hosts in memory. The first host of a site is its primary host, and the primary host is removed last (FCMS1665).
internal sealed class FakeSites : IManagementApi
{
    public List<JsonObject> Sites { get; } = [];

    public Dictionary<Guid, List<JsonObject>> Hosts { get; } = [];

    public List<ApiRequest> Requests { get; } = [];

    // Answers a request with a problem instead, when it gives one.
    public Func<ApiRequest, ApiProblemException?>? Refuse { get; set; }

    public void AddSite(Guid id, string name, string slug, bool isDefault = false)
    {
        Sites.Add(new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["slug"] = slug,
            ["defaultLocale"] = "en",
            ["isDefault"] = isDefault,
            ["createdAt"] = "2026-11-02T08:00:00+00:00",
        });
        Hosts[id] = [];
    }

    public void AddHost(Guid site, string hostname) =>
        Hosts[site].Add(new JsonObject { ["siteId"] = site, ["hostname"] = hostname, ["isPrimary"] = Hosts[site].Count == 0, ["createdAt"] = "2026-11-02T08:00:00+00:00" });

    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return new ApiResponse<T>(HttpStatusCode.OK, null, Answer(request).Deserialize(answer)!);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Answer(request);
        return new ApiResponse(HttpStatusCode.NoContent, null);
    }

    private JsonNode Answer(ApiRequest request)
    {
        Requests.Add(request);
        if (Refuse?.Invoke(request) is { } refused)
        {
            throw refused;
        }

        var segments = request.Path.Split('/').Select(Uri.UnescapeDataString).ToArray();
        return (request.Method.Method, segments) switch
        {
            ("GET", ["sites"]) => new JsonObject { ["items"] = new JsonArray([.. Sites.Select(site => site.DeepClone())]) },
            ("POST", ["sites"]) => Create(request.Body!),
            ("GET", ["sites", var id]) => Site(Guid.Parse(id)).DeepClone(),
            ("DELETE", ["sites", var id]) => Delete(Guid.Parse(id)),
            ("GET", ["sites", var id, "hosts"]) => new JsonObject { ["items"] = new JsonArray([.. HostsOf(Guid.Parse(id)).OrderByDescending(host => (bool)host["isPrimary"]!).Select(host => host.DeepClone())]) },
            ("POST", ["sites", var id, "hosts"]) => AddHost(Guid.Parse(id), request.Body!),
            ("PATCH", ["sites", var id, "hosts", var name]) => MakePrimary(Guid.Parse(id), name),
            ("DELETE", ["sites", var id, "hosts", var name]) => RemoveHost(Guid.Parse(id), name),
            _ => throw new InvalidOperationException($"No answer for {request.Method} {request.Path}."),
        };
    }

    private JsonObject Site(Guid id) =>
        Sites.FirstOrDefault(site => (Guid)site["id"]! == id) ?? throw MultiSiteTestContext.Problem(404, "FCMS2301", "site");

    private List<JsonObject> HostsOf(Guid id)
    {
        Site(id);
        return Hosts[id];
    }

    private JsonNode Create(JsonNode body)
    {
        var id = Guid.NewGuid();
        AddSite(id, body["name"]!.GetValue<string>(), body["slug"]!.GetValue<string>());
        Site(id)["defaultLocale"] = body["defaultLocale"]!.GetValue<string>();
        return Site(id).DeepClone();
    }

    private JsonObject Delete(Guid id)
    {
        Sites.Remove(Site(id));
        Hosts.Remove(id);
        return [];
    }

    private JsonNode AddHost(Guid id, JsonNode body)
    {
        var hosts = HostsOf(id);
        var host = new JsonObject
        {
            ["siteId"] = id,
            ["hostname"] = body["hostname"]!.GetValue<string>(),
            ["isPrimary"] = hosts.Count == 0 || body["isPrimary"]?.GetValue<bool>() == true,
            ["createdAt"] = "2026-11-20T08:00:00+00:00",
        };
        if ((bool)host["isPrimary"]!)
        {
            hosts.ForEach(other => other["isPrimary"] = false);
        }

        hosts.Add(host);
        return host.DeepClone();
    }

    private JsonNode MakePrimary(Guid id, string name)
    {
        var hosts = HostsOf(id);
        var host = hosts.FirstOrDefault(item => (string)item["hostname"]! == name) ?? throw MultiSiteTestContext.Problem(404, "FCMS1664");
        hosts.ForEach(item => item["isPrimary"] = item == host);
        return host.DeepClone();
    }

    private JsonObject RemoveHost(Guid id, string name)
    {
        var hosts = HostsOf(id);
        var host = hosts.FirstOrDefault(item => (string)item["hostname"]! == name) ?? throw MultiSiteTestContext.Problem(404, "FCMS1664");
        if ((bool)host["isPrimary"]! && hosts.Count > 1)
        {
            throw MultiSiteTestContext.Problem(409, "FCMS1665");
        }

        hosts.Remove(host);
        return [];
    }
}
