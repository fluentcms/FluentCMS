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

namespace FluentCMS.Plugins.Search.Admin.Tests;

// The half's page with the admin's services it injects: POST /search/rebuild behind IManagementApi, which answers the job it
// queued, the schema of the site, and the shell's context with what the caller holds for the instance.
public abstract class SearchTestContext : BunitContext
{
    internal static readonly Guid RebuildJob = new("0193a1b2-0000-7000-8000-0000000000b9");

    protected SearchTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Server);
        Services.AddSingleton<IAdminSchema>(Schema);
        Services.AddSingleton<IAdminContext>(Admin);
    }

    internal FakeSearch Server { get; } = new();

    internal FakeSchema Schema { get; } = new();

    internal FakeAdminContext Admin { get; } = new();

    protected static IElement Field<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent
    {
        var found = cut.FindAll("label").First(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label);
        return cut.Find("#" + found.GetAttribute("for"));
    }

    internal static ApiProblemException Problem(int status, string code) => new(new ApiProblem { Status = status, Code = code });
}

// The schema of the site: a type with a searchable field, one with none, and one named for display.
internal sealed class FakeSchema : IAdminSchema
{
    public bool Fails { get; set; }

    // Applied to the names of the types, which the sweep of the pseudo-cultures marks as a resource is.
    public Func<string, string> Label { get; set; } = name => name;

    public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default) => Fails
        ? Task.FromException<SchemaDescription>(new HttpRequestException("The server did not answer."))
        : Task.FromResult(new SchemaDescription
        {
            Types =
            [
                Type("article", null, searchable: true),
                Type("settings", null, searchable: false),
                Type("product", "Product page", searchable: true),
            ],
            Components = [],
            Blocks = [],
        });

    private SchemaTypeDescription Type(string name, string? displayName, bool searchable) => new()
    {
        Name = Label(name),
        Kind = "collection",
        DisplayName = displayName is null ? null : Label(displayName),
        Fields = [new SchemaFieldDescription { Name = "title", Type = "text", Validation = new FieldValidationDescription(), Searchable = searchable }],
    };
}

internal sealed class FakeAdminContext : IAdminContext, IAdminAccess
{
    public MeResponse? User => null;

    public MySiteInfo? Site => null;

    public string? ContentLocale => "en";

    public IAdminAccess Access => this;

    // The permissions the caller holds for the instance.
    public HashSet<string> ForInstance { get; } = [];

    public event Action? Changed;

    public bool Allows(string permission) => true;

    public bool AllowsForInstance(string permission) => ForInstance.Contains(permission);

    public TypeAccessInfo? ForType(string type) => null;

    public Task ReadSitesAsync(Guid? gone = null)
    {
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}

// POST /search/rebuild as the server answers it: the job it queued, or the problem Refuse gives.
internal sealed class FakeSearch : IManagementApi
{
    public List<ApiRequest> Requests { get; } = [];

    public Func<ApiRequest, Exception?>? Refuse { get; set; }

    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Requests.Add(request);
        if (Refuse?.Invoke(request) is { } refused)
        {
            throw refused;
        }

        if (request.Method != HttpMethod.Post || request.Path != "search/rebuild")
        {
            throw new InvalidOperationException($"No answer for {request.Method} {request.Path}.");
        }

        return new ApiResponse<T>(HttpStatusCode.Accepted, null, new JsonObject { ["jobId"] = RebuildJobId }.Deserialize(answer)!);
    }

    public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"No answer for {request.Method} {request.Path}.");

    private static Guid RebuildJobId => SearchTestContext.RebuildJob;
}
