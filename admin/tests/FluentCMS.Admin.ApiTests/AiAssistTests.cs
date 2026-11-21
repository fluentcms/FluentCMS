using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.AiAssist.Admin.Api;
using AiUsageQuery = FluentCMS.Plugins.AiAssist.Admin.Api.AiUsageQuery;
using FluentCMS.Samples.Blog;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The AI assist half's client against the test host (A-38): its records held to the OpenAPI document, the read of the fields
// the form offers its actions on, an action run against a stand-in of the provider as the built-in Editor, whose suggestion
// arrives in parts and leaves the entry as it was, the daily budget refused once and not sent again, and the usage that names
// the caller.
public sealed class AiAssistTests : IAsyncDisposable
{
    private const string Prefix = "/api/management/v1/";
    private const string SuperAdmin = "Super Admin";

    private readonly StandInChat _chat = new();
    private readonly FluentCmsTestHost _host;

    public AiAssistTests()
    {
        _host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithServices(services => services.AddSingleton(_chat))
            .Configure(cms =>
            {
                cms.AddBlogSample();
                cms.Providers.Use<IAiClientFactory, StandInFactory>();
            });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheRecordsOfTheHalfMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var ai = AiJsonContext.Default;
        List<string> gaps = [];

        OpenApiContractTests.Check(ai.AiUsageAnswer, OpenApiContractTests.Answer(paths, Prefix + "ai/usage", "get"), "ai/usage", document, gaps);
        OpenApiContractTests.Check(ai.AiActionAnswer, OpenApiContractTests.Answer(paths, Prefix + "ai/actions/{action}", "post"), "ai/actions/{action}", document, gaps);
        OpenApiContractTests.Check(ai.AiMediaAsset, OpenApiContractTests.Answer(paths, Prefix + "media/{id}", "get"), "media/{id}", document, gaps);
        gaps.ShouldBeEmpty();

        // What the form reads before the half loads (FieldsFrom), the body an action sends and the filters of the usage.
        var fields = Properties(document, OpenApiContractTests.Answer(paths, Prefix + "ai/fields", "get"));
        fields["fields"]!["type"]!.GetValue<string>().ShouldBe("object");
        fields["assets"]!["type"]!.GetValue<string>().ShouldBe("boolean");
        var body = Properties(document, paths[Prefix + "ai/actions/{action}"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!);
        body.Select(member => member.Key).ShouldBe(["type", "field", "locale", "text", "documentId", "options"], ignoreOrder: true);
        Properties(document, body["options"]!).Select(member => member.Key).ShouldBe(["targetLocale", "mediaId"], ignoreOrder: true);
        string[] filters = ["principalId", "action", "from", "to", "page", "pageSize"];
        filters.ShouldBeSubsetOf(paths[Prefix + "ai/usage"]!["get"]!["parameters"]!.AsArray().Select(parameter => Parameter(document, parameter!)["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task TheEditorRunsAnActionWhoseSuggestionArrivesInPartsAndTheUsageNamesThem()
    {
        await OptInAsync(dailyTokenBudget: 0);
        var article = await ArticleAsync("Ships are big boats.");
        using var editor = await SignInAsync("Editor");
        _chat.Pieces = ["Ships are ", "large vessels."];

        var offered = (await editor.Api.SendAsync(new ApiRequest(HttpMethod.Get, "ai/fields"), ManagementJsonContext.Default.JsonNode, Token)).Value;
        offered!["fields"]!["article"]!.AsArray().Select(name => name!.GetValue<string>()).ShouldBe(["title"]);

        List<string> pieces = [];
        var outcome = await new AiApi(editor.Api, editor.Events).RunAsync(
            "rewrite",
            new AiActionBody { Type = "article", Field = "title", Locale = "en", Text = "Ships are big boats." },
            pieces.Add,
            Token);

        pieces.ShouldBe(["Ships are ", "large vessels."]);
        outcome.Problem.ShouldBeNull();
        outcome.Suggestion!.GetValue<string>().ShouldBe("Ships are large vessels.");
        (await editor.Client.GetEntryAsync("article", article, null, Token)).Entry!.Fields["title"]!.GetValue<string>().ShouldBe("Ships are big boats.");

        using var admin = await SignInAsync(SuperAdmin);
        var usage = await new AiApi(admin.Api, admin.Events).UsageAsync(new AiUsageQuery { Action = "rewrite" }, Token);
        var row = usage.Items.ShouldHaveSingleItem();
        (row.Action, row.Type, row.Field, row.PrincipalKind, row.Succeeded).ShouldBe(("rewrite", "article", "title", "adminUser", true));
        row.PrincipalName.ShouldNotBeNullOrWhiteSpace();
        usage.Totals.Groups.ShouldHaveSingleItem().PrincipalId.ShouldBe(row.PrincipalId);
    }

    // FCMS1693 is a 429 that no wait lifts: the client sends it once and the shell shows no notice of its own.
    [Fact]
    public async Task TheDailyBudgetIsRefusedBeforeTheStreamAndIsNotSentAgain()
    {
        await OptInAsync(dailyTokenBudget: 1);
        using var editor = await SignInAsync("Editor");
        var ai = new AiApi(editor.Api, editor.Events);
        await ai.RunAsync("rewrite", new AiActionBody { Type = "article", Field = "title", Text = "Ships." }, _ => { }, Token);
        var sent = editor.Counts.GetValueOrDefault($"POST {Prefix}ai/actions/rewrite");

        var refused = await Should.ThrowAsync<ApiProblemException>(() => ai.RunAsync("rewrite", new AiActionBody { Type = "article", Field = "title", Text = "Ships." }, _ => { }, Token));

        (refused.Problem.Status, refused.Problem.Code).ShouldBe((429, "FCMS1693"));
        refused.Problem.Detail.ShouldNotBeNullOrWhiteSpace();
        editor.Counts[$"POST {Prefix}ai/actions/rewrite"].ShouldBe(sent + 1);
        editor.Notices.ShouldBeEmpty();
    }

    // A parameter by its reference to the shared ones of the document.
    private static JsonNode Parameter(JsonObject document, JsonNode parameter) =>
        parameter["$ref"]?.GetValue<string>() is { } reference ? document["components"]!["parameters"]![reference.Split('/')[^1]]! : parameter;

    // A schema by its reference, and a nullable one by its alternative that has members.
    private static JsonObject Properties(JsonObject document, JsonNode schema)
    {
        if ((schema["anyOf"] ?? schema["oneOf"]) is JsonArray alternatives)
        {
            schema = alternatives.First(alternative => alternative!["$ref"] is not null || alternative["properties"] is not null)!;
        }

        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }

    private async Task<AdminClient> SignInAsync(string role)
    {
        var tokens = await _host.SignInAsync(await _host.CreateUserAsync(role, cancellationToken: Token));
        return new AdminClient(tokens.AccessToken, () => ((TestServer)_host.Services.GetRequiredService<IServer>()).CreateHandler(), null, null);
    }

    // The settings of the default site, written as an administrator writes them: the stand-in provider and the title of an
    // article opted in.
    private async Task OptInAsync(int dailyTokenBudget)
    {
        using var admin = await SignInAsync(SuperAdmin);
        var read = await admin.Api.SendAsync(new ApiRequest(HttpMethod.Get, "settings/ai"), ManagementJsonContext.Default.JsonNode, Token);
        var settings = new JsonObject
        {
            ["provider"] = "openAi",
            ["model"] = "stand-in",
            ["apiKey"] = "not-sent",
            ["dailyTokenBudget"] = dailyTokenBudget,
            ["fields"] = new JsonObject { ["article"] = new JsonArray("title") },
        };
        await admin.Api.SendAsync(new ApiRequest(HttpMethod.Put, "settings/ai") { Body = settings, IfMatch = $"\"{read.Value!["version"]}\"" }, Token);
    }

    private async Task<Guid> ArticleAsync(string title)
    {
        using var admin = await SignInAsync(SuperAdmin);
        var created = await admin.Client.CreateEntryAsync("article", new JsonObject { ["title"] = title }, admin.Client.StartAction(), Token);
        return created.Entry!.Id;
    }

    // The provider of the tests: it answers Pieces, in parts when asked to stream, and calls nothing.
    private sealed class StandInChat : IChatClient
    {
        public IReadOnlyList<string> Pieces { get; set; } = ["A suggestion."];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(Pieces))) { Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 } });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var piece in Pieces)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 })]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StandInFactory(StandInChat chat) : IAiClientFactory
    {
        public ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(settings.Provider == AiProvider.None ? null : new AiClient(chat, "stand-in", settings.Model ?? "stand-in"));
    }
}
