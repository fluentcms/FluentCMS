using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// Problems, concurrency tags and idempotency keys, as the server answers them.
public sealed class WriteTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task A422BecomesAProblemWithThePathOfTheField()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() =>
            client.Client.CreateEntryAsync("tag", new JsonObject { ["name"] = new string('x', 60) }, client.Client.StartAction(), Token));

        thrown.Problem.Status.ShouldBe(422);
        thrown.Problem.Code.ShouldBe("FCMS2103");
        thrown.Problem.TraceId.ShouldNotBeNullOrEmpty();
        var error = thrown.Problem.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("name");
        error.Code.ShouldBe("FCMS2103");
    }

    [Fact]
    public async Task ASaveSendsTheTagOfItsReadAndAStaleTagIsA412WithTheEntryAsItIsNow()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var id = (await editor.Client.ListEntriesAsync("article", new ContentQuery { PageSize = 1 }, Token)).Items[0].Id;

        var read = await editor.Client.GetEntryAsync("article", id, "en", Token);
        var fields = read.Entry!.Fields.DeepClone().AsObject();
        fields["title"] = "Saved by the admin";
        var saved = await editor.Client.SaveEntryAsync("article", id, fields, read.EntityTag, "en", Token);
        var stale = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.SaveEntryAsync("article", id, fields, read.EntityTag, "en", Token));

        read.EntityTag.ShouldBe($"\"{read.Entry.Version}\"");
        saved.Value.Version.ShouldBe(read.Entry.Version + 1);
        saved.EntityTag.ShouldBe($"\"{saved.Value.Version}\"");
        saved.Value.Fields["title"]!.GetValue<string>().ShouldBe("Saved by the admin");
        stale.Problem.Status.ShouldBe(412);
        stale.Problem.Code.ShouldBe("FCMS3105");
        stale.Problem.Current!["version"]!.GetValue<int>().ShouldBe(saved.Value.Version);
        stale.Problem.Current["title"]!.GetValue<string>().ShouldBe("Saved by the admin");
    }

    [Fact]
    public async Task ALocaleTheEntryLacksIsReadAsNewAndASaveWithZeroAddsIt()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var created = await client.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "English only" }, client.Client.StartAction("en"), Token);

        var missing = await client.Client.GetEntryAsync("article", created.Value.Id, "fr", Token);
        var added = await client.Client.SaveEntryAsync("article", created.Value.Id, new JsonObject { ["title"] = "En français" }, missing.EntityTag, "fr", Token);
        var french = await client.Client.GetEntryAsync("article", created.Value.Id, "fr", Token);
        var again = await Should.ThrowAsync<ApiProblemException>(() =>
            client.Client.SaveEntryAsync("article", created.Value.Id, new JsonObject { ["title"] = "Encore" }, ApiRequest.NewLocale, "fr", Token));
        var gone = await Should.ThrowAsync<ApiProblemException>(() => client.Client.GetEntryAsync("article", Guid.NewGuid(), "fr", Token));

        missing.IsNewLocale.ShouldBeTrue();
        missing.EntityTag.ShouldBe(ApiRequest.NewLocale);
        added.Value.Locale.ShouldBe("fr");
        added.Value.Version.ShouldBe(1);
        french.Entry!.Fields["title"]!.GetValue<string>().ShouldBe("En français");
        french.EntityTag.ShouldBe("\"1\"");
        again.Problem.Status.ShouldBe(412);
        (gone.Problem.Status, gone.Problem.Code).ShouldBe((404, "FCMS3104"));
    }

    // Through the fallback chain the field would come from en and the read would have no tag; the form reads what fr holds.
    [Fact]
    public async Task ALocaleThatLacksAFieldOfAnotherIsReadWithItsOwnValuesAndATag()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var body = JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Only in English."}]}]}""");
        var created = await client.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Both", ["body"] = body }, client.Client.StartAction("en"), Token);
        await client.Client.SaveEntryAsync("article", created.Value.Id, new JsonObject { ["title"] = "Les deux", ["body"] = null }, ApiRequest.NewLocale, "fr", Token);

        var chain = await client.Client.ListEntriesAsync("article", new ContentQuery { Filter = new FieldFilter("title", FilterOperator.Equal, "Les deux"), Locale = "fr" }, Token);
        var read = await client.Client.GetEntryAsync("article", created.Value.Id, "fr", Token);
        var saved = await client.Client.SaveEntryAsync("article", created.Value.Id, read.Entry!.Fields, read.EntityTag, "fr", Token);

        chain.Items.ShouldHaveSingleItem().Fallbacks.ShouldNotBeNull().ShouldContainKey("body");
        read.Entry.Fallbacks.ShouldBeNull();
        read.EntityTag.ShouldBe("\"1\"");
        read.Entry.Fields["body"]!.ToJsonString().ShouldNotContain("Only in English.");
        saved.Value.Version.ShouldBe(2);
    }

    [Fact]
    public async Task ACreateWhoseAnswerWasLostReplaysUnderItsActionWithoutASecondEntry()
    {
        await using var admin = new AdminHost();
        var lose = new LoseTheFirstAnswer();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName, wrap: inner =>
        {
            lose.InnerHandler = inner;
            return lose;
        });
        client.Locale = "en";
        var action = client.Client.StartAction();
        var fields = new JsonObject { ["name"] = "Replayed", ["slug"] = "replayed" };

        await Should.ThrowAsync<HttpRequestException>(() => client.Client.CreateEntryAsync("tag", fields, action, Token));
        client.Locale = "fr";
        var created = await client.Client.CreateEntryAsync("tag", fields, action, Token);
        var found = await client.Client.ListEntriesAsync("tag", new ContentQuery { Filter = new FieldFilter("slug", FilterOperator.Equal, "replayed") }, Token);

        client.Counts["POST /api/management/v1/content/tag"].ShouldBe(2);
        found.Total.ShouldBe(1);
        found.Items.ShouldHaveSingleItem().Id.ShouldBe(created.Value.Id);
        created.Value.Fields["name"]!.GetValue<string>().ShouldBe("Replayed");
    }

    // Every name the server reserves is a system member of the admin's entry, so none of them stays in Fields.
    [Fact]
    public void TheSystemMembersAreTheNamesTheServerReserves()
    {
        ContentEntry.SystemMembers.ShouldBe(ContentItem.ReservedFieldNames, ignoreOrder: true);
        var everyName = new JsonObject(ContentItem.ReservedFieldNames.Select(name => KeyValuePair.Create<string, JsonNode?>(name, null)));
        everyName["title"] = "kept";

        var entry = JsonSerializer.Deserialize(everyName.ToJsonString(), ManagementJsonContext.Default.ContentEntry)!;

        entry.Fields.Select(field => field.Key).ShouldBe(["title"]);
    }

    // The server ran the first create, and the connection closed before its answer came back.
    private sealed class LoseTheFirstAnswer : DelegatingHandler
    {
        private int _lost;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.Method == HttpMethod.Post && Interlocked.Exchange(ref _lost, 1) == 0)
            {
                response.Dispose();
                throw new HttpRequestException("The connection closed before the answer came.");
            }

            return response;
        }
    }
}
