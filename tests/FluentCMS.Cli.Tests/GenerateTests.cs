using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Cli.Generate;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// `generate typescript`, `generate csharp` and `generate openapi` for the Blog sample, from an instance, a schema file and
// the host project.
public sealed class GenerateTests(BlogInstance blog) : IClassFixture<BlogInstance>, IDisposable
{
    private const string ClientNamespace = "FluentCMS.Client.Tests.Blog";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-generate-");

    private string Root => _directory.FullName;

    private static string Assembly => ToolRun.HostAssembly;

    public void Dispose() => _directory.Delete(recursive: true);

    [Theory]
    [InlineData("typescript")]
    [InlineData("csharp")]
    public async Task AnInstanceASchemaFileAndTheHostProjectWriteTheSameBytes(string language)
    {
        var schema = WriteSnapshot(blog.Snapshot);
        string[] options = language == "csharp" ? ["--namespace", ClientNamespace] : [];

        await GenerateAsync(["generate", language, "--output", Out("url"), "--url", BlogInstance.Url, "--token", blog.Token, .. options]);
        await GenerateAsync(["generate", language, "--output", Out("file"), "--schema", schema, .. options]);
        await GenerateAsync(["generate", language, "--output", Out("project"), "--assembly", Assembly, .. options]);

        var url = Read(Out("url"));
        url.Count.ShouldBeGreaterThan(0);
        Read(Out("file")).ShouldBe(url);
        Read(Out("project")).ShouldBe(url);
        url.Values.ShouldAllBe(text => text.Contains(BlogInstance.Schema(blog.Snapshot).Hash, StringComparison.Ordinal));
    }

    // The clients' tests compile and run the Blog code that is checked in, so it must be what the tool writes.
    [Fact]
    public async Task TheBlogCodeOfTheClientTestsIsWhatTheToolWrites()
    {
        var schema = WriteSnapshot(blog.Snapshot);

        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--check", "--output", Path.Combine(BlogInstance.RepositoryRoot, "tests", "FluentCMS.Client.Tests", "Blog")]);
        await GenerateAsync(["generate", "typescript", "--schema", schema, "--check", "--output", Path.Combine(BlogInstance.RepositoryRoot, "clients", "typescript", "test", "blog")]);
    }

    [Fact]
    public async Task CheckExitsWithOneAfterTheSchemaChangesAndWithZeroAfterGeneratingAgain()
    {
        var schema = WriteSnapshot(blog.Snapshot);
        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--output", Out("cs")]);
        await GenerateAsync(["generate", "typescript", "--schema", schema, "--output", Out("ts")]);
        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--output", Out("cs"), "--check"]);
        var before = Read(Out("cs"));

        // A new field in the author, and the tag gone.
        var changed = JsonNode.Parse(blog.Snapshot)!.AsObject();
        var collections = changed["collections"]!.AsArray();
        collections.Single(type => (string?)type!["name"] == "author")!["fields"]!.AsArray().Add(new JsonObject { ["name"] = "website", ["type"] = "url" });
        collections.Remove(collections.Single(type => (string?)type!["name"] == "tag"));
        changed["collections"]!.AsArray().Single(type => (string?)type!["name"] == "article")!["fields"]!.AsArray()
            .Remove(changed["collections"]!.AsArray().Single(type => (string?)type!["name"] == "article")!["fields"]!.AsArray().Single(field => (string?)field!["name"] == "tags"));
        File.WriteAllText(schema, changed.ToJsonString());

        var stale = await blog.RunAsync("generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--output", Out("cs"), "--check");
        stale.ExitCode.ShouldBe(1);
        stale.Error.ShouldStartWith("FCMS3372: ");
        stale.Error.ShouldContain("Author.g.cs differs");
        stale.Error.ShouldContain("Tag.g.cs is no longer generated");
        Read(Out("cs")).ShouldBe(before);

        (await blog.RunAsync("generate", "typescript", "--schema", schema, "--output", Out("ts"), "--check")).ExitCode.ShouldBe(1);

        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--output", Out("cs")]);
        await GenerateAsync(["generate", "typescript", "--schema", schema, "--output", Out("ts")]);
        File.Exists(Path.Combine(Out("cs"), "Tag.g.cs")).ShouldBeFalse();

        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", ClientNamespace, "--output", Out("cs"), "--check"]);
        await GenerateAsync(["generate", "typescript", "--schema", schema, "--output", Out("ts"), "--check"]);
    }

    [Fact]
    public void ABlockFieldIsAUnionInTypeScriptAndAClassHierarchyInCSharpAndLocalizedRelationAndMediaFieldsHaveTheirTypes()
    {
        var schema = BlogInstance.Schema(blog.Snapshot);
        var typeScript = TypeScriptEmitter.Emit(schema).Files.Single().Text;

        typeScript.ShouldContain("sections?: (HeroBlock | QuoteBlock | GalleryBlock)[] | null;");
        typeScript.ShouldContain("type: \"hero\";");
        typeScript.ShouldContain("author?: fluentcms.Relation<Author, \"author\"> | null;");
        typeScript.ShouldContain("tags?: (fluentcms.Relation<Tag, \"tag\">)[] | null;");
        typeScript.ShouldContain("hero?: fluentcms.MediaReference | null;");
        typeScript.ShouldContain("images?: (fluentcms.MediaReference)[] | null;");
        typeScript.ShouldContain("fallbacks?: { title?: string; slug?: string; body?: string; };");
        typeScript.ShouldContain("category?: \"news\" | \"opinion\" | \"video\" | null;");
        typeScript.ShouldContain("seo: new SeoComponentFields(\"seo\"),");
        typeScript.ShouldContain("article: client.collection<Article>(\"article\"),");

        var csharp = CSharpEmitter.Emit(schema, ClientNamespace).Files.ToDictionary(file => file.Name, file => file.Text);
        csharp["Block.g.cs"].ShouldContain("[global::System.Text.Json.Serialization.JsonDerivedType(typeof(HeroBlock), \"hero\")]");
        csharp["Block.g.cs"].ShouldContain("public class Block");
        csharp["HeroBlock.g.cs"].ShouldContain("public sealed class HeroBlock : Block");
        csharp["Article.g.cs"].ShouldContain("public global::System.Collections.Generic.List<Block>? Sections { get; set; }");
        csharp["Article.g.cs"].ShouldContain("public global::FluentCMS.Client.Relation<Author>? Author { get; set; }");
        csharp["Article.g.cs"].ShouldContain("public global::FluentCMS.Client.MediaReference? Hero { get; set; }");
        csharp["Article.g.cs"].ShouldContain("public ArticleFallbacks? Fallbacks { get; set; }");
        csharp["Article.g.cs"].ShouldContain("[global::FluentCMS.Client.ContentType(\"article\")]");
        csharp["Author.g.cs"].ShouldNotContain("Fallbacks");
        csharp["ArticleFields.g.cs"].ShouldContain("public static global::FluentCMS.Client.TextField Title { get; } = new global::FluentCMS.Client.TextField(\"title\");");
    }

    // The builders are those of the source generator, over the client's field types and in the client's namespace.
    [Fact]
    public void TheBuildersAreThoseTheSourceGeneratorEmitsInAHost()
    {
        var schema = BlogInstance.Schema(blog.Snapshot);
        var host = Generators.Emit.FieldsEmitter.Emit(Generators.Snapshot.SchemaDescriptions.Of(schema.Model, ClientNamespace), Generators.Emit.EmitOptions.Sdk);
        var client = CSharpEmitter.Emit(schema, ClientNamespace).Files.ToDictionary(file => file.Name, file => file.Text);

        foreach (var source in host.Sources)
        {
            var name = source.HintName[(ClientNamespace.Length + 1)..];
            client[name].Replace(CSharpEmitter.Header(schema.Hash) + "\n", "", StringComparison.Ordinal)
                .ShouldBe(source.Text.Replace("global::FluentCMS.Content.", "global::FluentCMS.Client.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task OpenApiSavesTheDocumentOfAnInstanceAndOfTheHostProject()
    {
        await using var instance = new OpenApiInstance();
        await instance.StartAsync();

        var fromInstance = Path.Combine(Root, "instance.json");
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            (await CommandLine.RunAsync(["generate", "openapi", "--output", fromInstance, "--url", BlogInstance.Url, "--token", "unused"], output, error, null, _ => new HttpClient(instance.Handler()), TestContext.Current.CancellationToken))
                .ShouldBe(0, error.ToString());
        }

        var fromProject = Path.Combine(Root, "project.json");
        await GenerateAsync(["generate", "openapi", "--output", fromProject, "--assembly", Assembly]);

        foreach (var file in new[] { fromInstance, fromProject })
        {
            var document = JsonNode.Parse(File.ReadAllText(file))!;
            ((string?)document["openapi"]).ShouldStartWith("3.1");
            document["paths"]!.AsObject().Select(path => path.Key).ShouldContain("/api/delivery/v1/article");
        }

        await GenerateAsync(["generate", "openapi", "--output", fromProject, "--assembly", Assembly, "--check"]);
        File.AppendAllText(fromProject, " ");
        (await blog.RunAsync("generate", "openapi", "--output", fromProject, "--assembly", Assembly, "--check")).ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task TheHostWritesItsDocumentWithTheHiddenCommand()
    {
        var file = Path.Combine(Root, "document.json");
        var result = await blog.Host.RunCommandAsync("openapi", "document", "--output", file);
        result.ExitCode.ShouldBe(0, result.Error);
        ((string?)JsonNode.Parse(File.ReadAllText(file))!["openapi"]).ShouldStartWith("3.1");

        (await blog.Host.RunCommandAsync("openapi", "document")).ExitCode.ShouldBe(2);
        (await blog.Host.RunCommandAsync("--help")).Output.ShouldNotContain("openapi document");
    }

    [Fact]
    public async Task BadInputIsRefusedWithItsCode()
    {
        var schema = WriteSnapshot(blog.Snapshot);

        var both = await blog.RunAsync("generate", "typescript", "--output", Root, "--schema", schema, "--url", BlogInstance.Url, "--token", blog.Token);
        both.ExitCode.ShouldBe(2);
        both.Error.ShouldStartWith("FCMS3377: ");

        var keyword = await blog.RunAsync("generate", "csharp", "--output", Root, "--schema", schema, "--namespace", "My.class");
        keyword.ExitCode.ShouldBe(2);
        keyword.Error.ShouldStartWith("FCMS3374: ");

        (await blog.RunAsync("generate", "csharp", "--output", Root, "--schema", schema)).ExitCode.ShouldBe(2);

        File.WriteAllText(schema, """{ "collections": [ { "name": "post", "fields": [ { "name": "id", "type": "text" } ] } ] }""");
        var invalid = await blog.RunAsync("generate", "typescript", "--output", Root, "--schema", schema);
        invalid.ExitCode.ShouldBe(1);
        invalid.Error.ShouldStartWith("FCMS3370: ");

        var refused = await blog.RunAsync("generate", "typescript", "--output", Root, "--url", BlogInstance.Url, "--token", "fcms_mk_not-a-key");
        refused.ExitCode.ShouldBe(1);
        refused.Error.ShouldStartWith("FCMS3330: ");
        refused.Error.ShouldContain("401");
        refused.Error.ShouldNotContain("not-a-key");
    }

    // A field type of a plugin that the emitters do not know is JSON in C# and unknown in TypeScript, with a warning.
    [Fact]
    public void AFieldTypeWithoutATypeIsJsonWithAWarning()
    {
        var schema = BlogInstance.Schema("""{ "collections": [ { "name": "place", "fields": [ { "name": "where", "type": "acme.geo-point_2", "radius": 5 } ] } ] }""");

        var typeScript = TypeScriptEmitter.Emit(schema);
        typeScript.Files.Single().Text.ShouldContain("/** acme.geo-point_2 */\n    where?: unknown | null;");
        typeScript.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3375" && diagnostic.Message.Contains("'acme.geo-point_2'", StringComparison.Ordinal));

        var csharp = CSharpEmitter.Emit(schema, "Places");
        var place = csharp.Files.Single(file => file.Name == "Place.g.cs").Text;
        place.ShouldContain("/// <summary>The field <c>where</c> (acme.geo-point_2).</summary>");
        place.ShouldContain("public global::System.Text.Json.JsonElement? Where { get; set; }");
        csharp.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3375" && diagnostic.Message.Contains("'acme.geo-point_2'", StringComparison.Ordinal));
    }

    // A field type is text of the snapshot that the emitters write into comments, so one that is not a name is refused
    // before anything is written, whether the snapshot comes from a file or from an instance.
    [Theory]
    [InlineData("typescript")]
    [InlineData("csharp")]
    public async Task AFieldTypeThatIsNotANameIsRefusedAndNothingIsWritten(string language)
    {
        string[] options = language == "csharp" ? ["--namespace", "Posts"] : [];
        foreach (var fieldType in InjectedFieldTypes)
        {
            var snapshot = Snapshot(fieldType);
            var fromFile = await blog.RunAsync(["generate", language, "--output", Out("file"), "--schema", WriteSnapshot(snapshot), .. options]);
            var fromInstance = await RunAgainstAsync(snapshot, ["generate", language, "--output", Out("url"), "--url", BlogInstance.Url, "--token", "fcms_mk_unused", .. options]);

            foreach (var (exitCode, error) in new[] { (fromFile.ExitCode, fromFile.Error), fromInstance })
            {
                exitCode.ShouldBe(1);
                error.ShouldStartWith("FCMS3370: ");
                error.ShouldContain("the field 'post.title' has a field type that is not a name");
                error.ShouldNotContain("njected");
            }

            Directory.Exists(Out("file")).ShouldBeFalse();
            Directory.Exists(Out("url")).ShouldBeFalse();
        }
    }

    // The emitters check a field type themselves too, so a model that did not come through ClientSchema.Read writes none
    // that is not a name, in code or in a warning.
    [Fact]
    public void TheEmittersWriteNoFieldTypeThatIsNotAName()
    {
        foreach (var fieldType in InjectedFieldTypes)
        {
            var schema = new ClientSchema(new SchemaBuilder(new UncheckedFieldTypes()).AddSchemaJson(Snapshot(fieldType)).Build(), "hash");

            var typeScript = TypeScriptEmitter.Emit(schema);
            typeScript.Files.Single().Text.ShouldContain("    title?: unknown | null;");
            var csharp = CSharpEmitter.Emit(schema, "Posts");
            csharp.Files.Single(file => file.Name == "Post.g.cs").Text.ShouldContain("/// <summary>The field <c>title</c>.</summary>");

            foreach (var code in new[] { typeScript, csharp })
            {
                code.Files.ShouldAllBe(file => !file.Text.Contains("njected", StringComparison.Ordinal));
                code.Diagnostics.ShouldAllBe(diagnostic => !diagnostic.Message.Contains("njected", StringComparison.Ordinal));
                code.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3375" && diagnostic.Message.Contains("'post.title'", StringComparison.Ordinal));
            }
        }
    }

    // The field types of the host, core's and the first-party plugins', pass the check, so they generate as before.
    [Fact]
    public void TheFieldTypesOfCoreAndOfThePluginsAreNames()
    {
        var fieldTypes = blog.Host.Services.GetRequiredService<IFieldTypeRegistry>().All.Select(fieldType => fieldType.Name).ToList();

        fieldTypes.ShouldContain("media");
        fieldTypes.ShouldAllBe(name => FieldTypeNames.IsValid(name));
    }

    // Names that clash with an export of @fluentcms/client, a member of BlocksField or object, or a builder of another type.
    [Fact]
    public void NamesThatClashGetNothingOrAreQualified()
    {
        var schema = BlogInstance.Schema("""
            {
              "collections": [
                { "name": "article", "fields": [ { "name": "title", "type": "text" }, { "name": "equals", "type": "text" }, { "name": "parts", "type": "blocks", "blocks": ["filter", "sort"] } ] },
                { "name": "article-fields", "fields": [ { "name": "title", "type": "text" } ] },
                { "name": "relation", "fields": [ { "name": "entry", "type": "text" } ] }
              ],
              "singletons": [ { "name": "site-settings", "fields": [ { "name": "name", "type": "text" } ] } ],
              "blocks": [
                { "name": "filter", "fields": [ { "name": "label", "type": "text" } ] },
                { "name": "sort", "fields": [ { "name": "label", "type": "text" } ] }
              ]
            }
            """);

        var typeScript = TypeScriptEmitter.Emit(schema);
        var module = typeScript.Files.Single().Text;
        module.ShouldContain("import * as fluentcms from \"@fluentcms/client\";");
        module.ShouldContain("export interface Relation extends fluentcms.Entry {");
        module.ShouldContain("get filter(): FilterBlockFields {");
        module.ShouldContain("get sort(): SortBlockFields {");
        module.ShouldContain("export const ArticleFields = {");
        module.ShouldNotContain("export interface ArticleFields");
        module.ShouldContain("siteSettings: client.singleton<SiteSettings>(\"site-settings\"),");
        typeScript.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3376" && diagnostic.Message.Contains("'article-fields'", StringComparison.Ordinal));

        var csharp = CSharpEmitter.Emit(schema, "Clashes");
        csharp.Files.Single(file => file.Name == "ArticleFields.g.cs").Text.ShouldContain("public static class ArticleFields");
        csharp.Files.Single(file => file.Name == "Article.g.cs").Text.ShouldNotContain(" Equals ");
        csharp.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3376" && diagnostic.Message.Contains("'article.equals'", StringComparison.Ordinal));
        csharp.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == "FCMS3376" && diagnostic.Message.Contains("'article-fields'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFileWhoseClassWasRenamedOnlyInCaseKeepsItsNewName()
    {
        var directory = Out("case");
        Directory.CreateDirectory(directory);
        var schema = WriteSnapshot("""{ "collections": [ { "name": "foobar", "fields": [ { "name": "title", "type": "text" } ] } ] }""");
        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", "Cases", "--output", directory]);

        File.WriteAllText(schema, """{ "collections": [ { "name": "foo-bar", "fields": [ { "name": "title", "type": "text" } ] } ] }""");
        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", "Cases", "--output", directory]);

        Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["FooBar.g.cs", "FooBarFields.g.cs"]);
        await GenerateAsync(["generate", "csharp", "--schema", schema, "--namespace", "Cases", "--output", directory, "--check"]);
    }

    // A field type that closes the TypeScript comment, one whose line break ends the C# comment before a module
    // initializer, and one that opens an XML element in it.
    private static readonly string[] InjectedFieldTypes =
    [
        "x */ } console.log(\"injected\"); export interface Injected { /*",
        "x)\n[global::System.Runtime.CompilerServices.ModuleInitializer] internal static void Injected() { }\n/// (",
        "x<see cref=\"Injected\"/>",
    ];

    private static string Snapshot(string fieldType) => new JsonObject
    {
        ["collections"] = new JsonArray(new JsonObject
        {
            ["name"] = "post",
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = fieldType }),
        }),
    }.ToJsonString();

    // Runs the tool against an instance that answers every request with the snapshot given.
    private static async Task<(int ExitCode, string Error)> RunAgainstAsync(string snapshot, string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandLine.RunAsync(args, output, error, null, _ => new HttpClient(new SnapshotAnswer(snapshot)), TestContext.Current.CancellationToken);
        return (exitCode, error.ToString());
    }

    private async Task GenerateAsync(string[] args)
    {
        var result = await blog.RunAsync(args);
        result.ExitCode.ShouldBe(0, result.Error);
    }

    private string WriteSnapshot(string snapshot)
    {
        var path = Path.Combine(Root, $"schema-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, snapshot);
        return path;
    }

    private string Out(string name) => Path.Combine(Root, name);

    private static Dictionary<string, string> Read(string directory) =>
        Directory.EnumerateFiles(directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

    private sealed class SnapshotAnswer(string snapshot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(snapshot, Encoding.UTF8, "application/json") });
    }
}
