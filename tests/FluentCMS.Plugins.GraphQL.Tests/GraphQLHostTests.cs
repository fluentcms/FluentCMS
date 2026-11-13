using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public async Task AWriteWhoseSelectionPopulatesDeeperThanTheCapIsRefusedBeforeItWrites()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);
        string? id = null;
        foreach (var title in (string[])["One", "Two", "Three", "Four", "Five"])
        {
            id = (await admin.QueryAsync("mutation($input: NoteInput!) { createNote(input: $input) { id } }", new { input = new { title, parent = id } }))["createNote"]!["id"]!.GetValue<string>();
        }

        // Four levels of population, one over the cap of 3, and three.
        const string Deep = "parent { parent { parent { parent { title } } } }";
        const string Allowed = "parent { parent { parent { title } } }";

        // A query with the selection is refused before it reads, and a write as it, before it writes.
        var (_, query) = await admin.FailAsync($"query($id: ID) {{ note(id: $id, status: DRAFT) {{ {Deep} }} }}", new { id });
        query.Codes().ShouldBe(["FCMS2207"]);

        await RefusedAsync("createNote", $"createNote(input: {{ title: \"Six\", parent: $id }}) {{ id {Deep} }}");
        (await admin.QueryAsync("{ noteList(status: DRAFT) { total } }"))["noteList"]!["total"]!.GetValue<long>().ShouldBe(6);
        var created = (await admin.QueryAsync($"mutation($id: ID!) {{ createNote(input: {{ title: \"Six\", parent: $id }}) {{ title {Allowed} }} }}", new { id }))["createNote"]!;
        created["parent"]!["parent"]!["parent"]!["title"]!.GetValue<string>().ShouldBe("Three");

        await RefusedAsync("updateNote", $"updateNote(id: $id, input: {{ title: \"Changed\" }}, expectedVersion: 1) {{ {Deep} }}");
        (await DraftAsync())["version"]!.GetValue<int>().ShouldBe(1);
        var updated = (await admin.QueryAsync($"mutation($id: ID!) {{ updateNote(id: $id, input: {{ title: \"Changed\" }}, expectedVersion: 1) {{ version {Allowed} }} }}", new { id }))["updateNote"]!;
        updated["version"]!.GetValue<int>().ShouldBe(2);
        updated["parent"]!["parent"]!["parent"]!["title"]!.GetValue<string>().ShouldBe("Two");

        await RefusedAsync("publishNote", $"publishNote(id: $id, expectedVersion: 2) {{ entry {{ {Deep} }} }}");
        (await PublishedAsync()).ShouldBeNull();
        var published = (await admin.QueryAsync($"mutation($id: ID!) {{ publishNote(id: $id, expectedVersion: 2) {{ entry {{ status {Allowed} }} }} }}", new { id }))["publishNote"]!["entry"]!;
        published["status"]!.GetValue<string>().ShouldBe("PUBLISHED");
        published["parent"]!["parent"]!["parent"]!["title"]!.GetValue<string>().ShouldBe("Two");

        await RefusedAsync("unpublishNote", $"unpublishNote(id: $id) {{ {Deep} }}");
        (await PublishedAsync()).ShouldNotBeNull();
        var unpublished = (await admin.QueryAsync($"mutation($id: ID!) {{ unpublishNote(id: $id) {{ title {Allowed} }} }}", new { id }))["unpublishNote"]!;
        unpublished["parent"]!["parent"]!["parent"]!["title"]!.GetValue<string>().ShouldBe("Two");
        (await PublishedAsync()).ShouldBeNull();
        (await DraftAsync())["version"]!.GetValue<int>().ShouldBe(2);

        async Task RefusedAsync(string field, string mutation)
        {
            var (data, errors) = await admin.FailAsync($"mutation($id: ID!) {{ {mutation} }}", new { id });
            data![field].ShouldBeNull();
            errors.Codes().ShouldBe(query.Codes(), field);
            errors[0]!["extensions"]!["status"]!.GetValue<int>().ShouldBe(400);
            errors[0]!["extensions"]!["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("populate");
            errors[0]!["path"]!.AsArray().Select(part => part!.ToString()).ShouldBe([field]);
        }

        async Task<JsonNode> DraftAsync() => (await admin.QueryAsync("query($id: ID) { note(id: $id, status: DRAFT) { title version } }", new { id }))["note"]!;

        async Task<JsonNode?> PublishedAsync() => (await admin.QueryAsync("query($id: ID) { note(id: $id) { id } }", new { id }))["note"];
    }

    [Fact]
    public async Task TheDepthOfASelectionCountsRelationsAndRepeatedComponentsAndNotASingleComponent()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);
        string? id = null;
        foreach (var title in (string[])["One", "Two", "Three", "Four"])
        {
            var input = new { title, parent = id, links = new[] { new { label = title } }, aside = new { label = title, note = id } };
            id = (await admin.QueryAsync("mutation($input: NoteInput!) { createNote(input: $input) { id } }", new { input }))["createNote"]!["id"]!.GetValue<string>();
        }

        // A repeated component is a level of its own, a single one is not, and each alias of a field is counted on its own.
        var total = 5;
        foreach (var selection in (string[])[
            "parent { parent { parent { links { label } } } }",
            "parent { parent { aside { note { links { label } } } } }",
            "parent { shallow: parent { links { label } } deep: parent { parent { links { label } } } }"])
        {
            var (data, errors) = await admin.FailAsync($"mutation($id: ID!) {{ createNote(input: {{ title: \"Five\", parent: $id }}) {{ {selection} }} }}", new { id });
            data!["createNote"].ShouldBeNull(selection);
            errors.Codes().ShouldBe(["FCMS2207"], selection);
            (await TotalAsync()).ShouldBe(total, selection);
        }

        (await CreateAsync("parent { parent { links { label } } }"))["parent"]!["parent"]!["links"].Strings("label").ShouldBe(["Three"]);
        (await CreateAsync("parent { aside { note { links { label } } } }"))["parent"]!["aside"]!["note"]!["links"].Strings("label").ShouldBe(["Three"]);
        var aliased = (await CreateAsync("parent { shallow: parent { links { label } } deep: parent { parent { title } } }"))["parent"]!;
        aliased["shallow"]!["links"].Strings("label").ShouldBe(["Three"]);
        aliased["deep"]!["parent"]!["title"]!.GetValue<string>().ShouldBe("Two");
        (await TotalAsync()).ShouldBe(total);

        async Task<JsonNode> CreateAsync(string selection)
        {
            total++;
            return (await admin.QueryAsync($"mutation($id: ID!) {{ createNote(input: {{ title: \"Five\", parent: $id }}) {{ {selection} }} }}", new { id }))["createNote"]!;
        }

        async Task<long> TotalAsync() => (await admin.QueryAsync("{ noteList(status: DRAFT) { total } }"))["noteList"]!["total"]!.GetValue<long>();
    }

    [Fact]
    public async Task AnUnexpectedFailureOfTheReadAfterAWriteIsLoggedOnceAndReportedAtEachFieldItWasToPopulate()
    {
        var logs = new LogCapture();
        await using var host = Host(configure: services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddScoped(provider => FailingPopulation.Wrap(provider.GetRequiredService<ContentService>()));
        });
        using var admin = await AdminAsync(host);
        var one = (await admin.QueryAsync("mutation { createNote(input: { title: \"One\" }) { id } }"))["createNote"]!["id"]!.GetValue<string>();

        var (data, errors) = await admin.FailAsync(
            "mutation($id: ID!) { createNote(input: { title: \"Full\", parent: $id, aside: { label: \"Aside\", note: $id } }) { title parent { title } aside { label note { title } } } }",
            new { id = one });
        var created = data!["createNote"]!;
        created["title"]!.GetValue<string>().ShouldBe("Full");
        created["parent"].ShouldBeNull();
        created["aside"]!["label"]!.GetValue<string>().ShouldBe("Aside");
        created["aside"]!["note"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS3199", "FCMS3199"]);
        errors.Select(error => string.Join('.', error!["path"]!.AsArray().Select(part => part!.ToString()))).ShouldBe(["createNote.parent", "createNote.aside.note"], ignoreOrder: true);
        errors.ToJsonString().ShouldNotContain("The read failed.");

        // One read failed, so one line, whatever the number of fields that report it.
        logs.Lines.Where(line => line.Id == 9321).Select(line => line.Message).ShouldBe(["The GraphQL field createNote failed and was answered as FCMS3199."]);
    }

    [Fact]
    public async Task AWriteWhoseReadFailsAfterItCommittedAnswersTheWrittenEntryWithTheErrorAtTheField()
    {
        // One related entry at most in a read, so that a read that populates two fails once the write is done.
        var limit = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MaxPopulatedEntries"] = "1" }).Build();
        await using var host = Host(configure: services => services.Configure<ContentQueryOptions>(limit));
        using var admin = await AdminAsync(host);
        var one = (await admin.QueryAsync("mutation { createNote(input: { title: \"One\" }) { id } }"))["createNote"]!["id"]!.GetValue<string>();
        var two = (await admin.QueryAsync("mutation($id: ID!) { createNote(input: { title: \"Two\", parent: $id }) { id } }", new { id = one }))["createNote"]!["id"]!.GetValue<string>();

        // Each write is done and answers the entry it wrote; the relation the read was to populate is null with its error.
        const string Selection = "id title version status parent { title parent { title } }";
        var created = await WrittenAsync(["createNote"], $"createNote(input: {{ title: \"Three\", parent: $id }}) {{ {Selection} }}", two);
        created["title"]!.GetValue<string>().ShouldBe("Three");
        var id = created["id"]!.GetValue<string>();
        (await WrittenAsync(["updateNote"], $"updateNote(id: $id, input: {{ title: \"Three again\" }}, expectedVersion: 1) {{ {Selection} }}", id))["version"]!.GetValue<int>().ShouldBe(2);
        (await WrittenAsync(["publishNote", "entry"], $"publishNote(id: $id, expectedVersion: 2) {{ entry {{ {Selection} }} }}", id))["status"]!.GetValue<string>().ShouldBe("PUBLISHED");
        (await WrittenAsync(["unpublishNote"], $"unpublishNote(id: $id) {{ {Selection} }}", id))["title"]!.GetValue<string>().ShouldBe("Three again");

        var draft = (await admin.QueryAsync("query($id: ID) { note(id: $id, status: DRAFT) { title version parent { id } } }", new { id }))["note"]!;
        draft["title"]!.GetValue<string>().ShouldBe("Three again");
        draft["parent"]!["id"]!.GetValue<string>().ShouldBe(two);
        (await admin.QueryAsync("query($id: ID) { note(id: $id) { id } }", new { id }))["note"].ShouldBeNull();

        async Task<JsonNode> WrittenAsync(string[] path, string mutation, string target)
        {
            var (data, errors) = await admin.FailAsync($"mutation($id: ID!) {{ {mutation} }}", new { id = target });
            var entry = path.Aggregate(data!, (node, name) => node[name]!);
            entry["parent"].ShouldBeNull();
            errors.Codes().ShouldBe(["FCMS2207"], path[0]);
            errors.Single()!["path"]!.AsArray().Select(part => part!.ToString()).ShouldBe([.. path, "parent"]);
            return entry;
        }
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
            .WithContentTypes(typeof(Note), typeof(SiteSettings), typeof(Link), typeof(Aside))
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
