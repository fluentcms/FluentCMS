using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.GraphQL.Tests;

// The tests that need a host of their own: its options, its content types or the plugin's state in its site.
public sealed class GraphQLHostTests : IDisposable
{
    private const string Endpoint = BlogHost.Endpoint;

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("fluentcms-graphql-queries-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public async Task WithOnlyPersistedAQueryThatIsNotInTheFolderIsRefused()
    {
        File.WriteAllText(Path.Combine(_folder.FullName, "notes.graphql"), "query Notes($title: String) { noteList(filter: { title: { eq: $title } }, status: DRAFT) { items { title } } }");
        await using var host = Host(options =>
        {
            options.PersistedQueries = _folder.FullName;
            options.OnlyPersisted = true;
        });
        using var admin = await AdminAsync(host);

        var (_, refused) = await admin.SendAsync("{ noteList { items { title } } }");
        refused["data"].ShouldBeNull();
        refused["errors"]!.AsArray().Codes().ShouldBe(["FCMS1646"]);

        using var unknown = await admin.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { id = "missing" }, Token);
        JsonNode.Parse(await unknown.Content.ReadAsStringAsync(Token))!["errors"]!.AsArray().Codes().ShouldBe(["FCMS1647"]);

        // The mutation below is not in the folder either, so the note is seeded.
        using var persisted = await admin.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { id = "notes", variables = new { title = "Seeded" } }, Token);
        var body = JsonNode.Parse(await persisted.Content.ReadAsStringAsync(Token))!;
        persisted.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        body["data"]!["noteList"]!["items"].Strings("title").ShouldBe(["Seeded"]);
    }

    [Fact]
    public async Task APersistedQueryAndAnyOtherAreBothAnsweredWithoutOnlyPersisted()
    {
        File.WriteAllText(Path.Combine(_folder.FullName, "typename.graphql"), "{ __typename }");
        await using var host = Host(options => options.PersistedQueries = _folder.FullName);
        using var anonymous = host.CreateClient();

        using var persisted = await anonymous.PostAsJsonAsync(new Uri(Endpoint, UriKind.Relative), new { id = "typename" }, Token);
        JsonNode.Parse(await persisted.Content.ReadAsStringAsync(Token))!["data"]!["__typename"]!.GetValue<string>().ShouldBe("Query");
        (await anonymous.QueryAsync("{ __typename }"))["__typename"]!.GetValue<string>().ShouldBe("Query");
    }

    [Fact]
    public async Task TheEndpointAnswers404ForASiteWhereThePluginIsDisabled()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);
        using var anonymous = host.CreateClient();
        (await anonymous.QueryAsync("{ __typename }"))["__typename"]!.GetValue<string>().ShouldBe("Query");

        await EnableAsync(admin, enabled: false);
        foreach (var client in (HttpClient[])[anonymous, admin])
        {
            var (status, problem) = await client.SendAsync("{ __typename }");
            status.ShouldBe(HttpStatusCode.NotFound);
            problem["code"]!.GetValue<string>().ShouldBe("FCMS1550");
        }

        await EnableAsync(admin, enabled: true);
        (await anonymous.QueryAsync("{ __typename }"))["__typename"]!.GetValue<string>().ShouldBe("Query");

        static async Task EnableAsync(HttpClient admin, bool enabled)
        {
            using var response = await admin.PutAsJsonAsync(new Uri($"/api/management/v1/plugins/graphql/sites/{DefaultSite.Id}", UriKind.Relative), new { enabled }, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        }
    }

    [Fact]
    public async Task ThePluginIsListedWithItsEndpoint()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);

        using var response = await admin.GetAsync(new Uri("/api/management/v1/plugins", UriKind.Relative), Token);
        var plugin = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["items"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "graphql")!;

        plugin["contributions"]!["endpoints"]!.AsArray().Select(endpoint => endpoint!["route"]!.GetValue<string>().TrimEnd('/')).ShouldContain(Endpoint);
    }

    [Fact]
    public async Task ASingletonAndEveryShapeOfFieldAreReadAndWritten()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);
        var note = (await admin.QueryAsync("mutation { createNote(input: { title: \"Pinned\" }) { id } }"))["createNote"]!["id"]!.GetValue<string>();

        string[] colors = ["teal", "sand"];
        string[] channels = ["WEB", "PRINT"];
        const string Fields = "id status tagline visitors rating live founded theme channels variant links { label target { id type } }";
        var created = (await admin.QueryAsync(
            $"mutation($input: SiteSettingsInput!) {{ createSiteSettings(input: $input) {{ {Fields} }} }}",
            new
            {
                input = new
                {
                    tagline = "Content first",
                    visitors = 5_000_000_000L,
                    rating = 4.25m,
                    live = true,
                    founded = "2026-11-10",
                    theme = new { colors, dark = false },
                    channels,
                    variant = "a_b",
                    links = new[] { new { label = "Pinned note", target = note } },
                },
            }))["createSiteSettings"]!;

        created["visitors"]!.GetValue<long>().ShouldBe(5_000_000_000L);
        created["rating"]!.GetValue<decimal>().ShouldBe(4.25m);
        created["live"]!.GetValue<bool>().ShouldBeTrue();
        created["founded"]!.GetValue<string>().ShouldBe("2026-11-10");
        created["theme"]!["colors"]!.AsArray().Select(color => color!.GetValue<string>()).ShouldBe(colors);
        created["channels"]!.AsArray().Select(channel => channel!.GetValue<string>()).ShouldBe(channels);
        created["variant"]!.GetValue<string>().ShouldBe("a_b");

        // A relation inside a repeated component is stored with it and stays a reference.
        created["links"]![0]!["label"]!.GetValue<string>().ShouldBe("Pinned note");
        created["links"]![0]!["target"]!["id"]!.GetValue<string>().ShouldBe(note);
        created["links"]![0]!["target"]!["type"]!.GetValue<string>().ShouldBe("note");

        // The singleton is a field of its own, with no list beside it, and reads what REST reads.
        var read = (await admin.QueryAsync($"{{ siteSettings(status: DRAFT) {{ {Fields} }} }}"))["siteSettings"]!;
        read.ToJsonString().ShouldBe(created.ToJsonString());
        var rest = await GraphQLQueryTests.RestAsync(admin, "/api/management/v1/content/site-settings");
        rest["items"]![0]!["rating"]!.GetValue<decimal>().ShouldBe(4.25m);
        rest["items"]![0]!["channels"]!.AsArray().Select(channel => channel!.GetValue<string>()).ShouldBe(["web", "print"]);

        (await admin.QueryAsync("{ siteSettings { id } }"))["siteSettings"].ShouldBeNull();
        (await admin.QueryAsync("""{ __type(name: "Query") { fields { name } } }"""))["__type"]!["fields"].Strings("name")
            .ShouldBe(["note", "noteList", "siteSettings"], ignoreOrder: true);

        // A hidden field is in no type, and a read-only one in no input.
        var settings = (await admin.QueryAsync("""{ output: __type(name: "SiteSettings") { fields { name } } input: __type(name: "SiteSettingsInput") { inputFields { name } } }"""));
        settings["output"]!["fields"].Strings("name").ShouldContain("code");
        settings["output"]!["fields"].Strings("name").ShouldNotContain("secret");
        settings["input"]!["inputFields"].Strings("name").ShouldNotContain("code");
        settings["input"]!["inputFields"].Strings("name").ShouldNotContain("secret");
    }

    [Fact]
    public async Task ARequestThatRunsLongerThanTheTimeoutIsRefused()
    {
        await using var host = Host(
            options => options.ExecutionTimeout = TimeSpan.FromMilliseconds(100),
            services => services.AddSingleton<IGraphQLSchemaContributor, SlowContributor>());
        using var anonymous = host.CreateClient();

        var (_, body) = await anonymous.SendAsync("{ never }");

        body["errors"]!.AsArray().Codes().ShouldBe(["FCMS1648"]);
    }

    [Fact]
    public async Task AHostWithNoContentTypesStillAnswers()
    {
        await using var host = new FluentCmsTestHost();
        using var anonymous = host.CreateClient();

        (await anonymous.QueryAsync("{ __typename _empty }"))["__typename"]!.GetValue<string>().ShouldBe("Query");
    }

    [Fact]
    public void OptionsThatAreNotValidStopTheHost()
    {
        using var onlyPersisted = Host(options => options.OnlyPersisted = true);
        Should.Throw<Exception>(() => onlyPersisted.Services).ToString().ShouldContain("FCMS1640");

        using var noFolder = Host(options => options.PersistedQueries = Path.Combine(_folder.FullName, "missing"));
        Should.Throw<Exception>(() => noFolder.Services).ToString().ShouldContain("FCMS1640");

        using var noDepth = Host(options => options.MaxDepth = 0);
        Should.Throw<Exception>(() => noDepth.Services).ToString().ShouldContain("FCMS1640");
    }

    [Fact]
    public void TwoContentTypesWithOneGraphQLNameStopTheHost()
    {
        using var host = new FluentCmsTestHost().WithContentTypes<Thing, ThingList>();

        var exception = Should.Throw<Exception>(() => host.Services).ToString();

        exception.ShouldContain("FCMS1641");
        exception.ShouldContain("ThingList");
    }

    private static FluentCmsTestHost Host(Action<GraphQLOptions>? options = null, Action<IServiceCollection>? configure = null) =>
        new FluentCmsTestHost()
            .WithContentTypes(typeof(Note), typeof(SiteSettings), typeof(Link))
            .WithServices(services =>
            {
                if (options is not null)
                {
                    services.PostConfigure(options);
                }

                configure?.Invoke(services);
            })
            .Seed(new Note { Title = "Seeded" });

    private static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
}
