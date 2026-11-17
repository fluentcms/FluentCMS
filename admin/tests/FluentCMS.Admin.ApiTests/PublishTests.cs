using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Publishing;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The status actions of the form (A-16) as the server answers them: publish, unpublish and discard of one locale, each
// answer the read the form goes on with, and the warnings or the 422 about unpublished references.
public sealed class PublishTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.EditorName)]
    [InlineData(BuiltInRoles.SuperAdminName)]
    public async Task PublishThenAChangeIsModifiedDiscardGoesBackAndUnpublishWithdraws(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var access = (await client.Client.GetMyAccessAsync(null, null, Token)).Types["article"];
        (access.Publish, access.Unpublish, access.Update).ShouldBe((true, true, true));
        var created = await client.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Published by the admin" }, client.Client.StartAction("en"), Token);
        var id = created.Entry!.Id;

        var published = await client.Client.PublishEntryAsync("article", id, created, Token);
        var fields = published.Read.Entry!.Fields.DeepClone().AsObject();
        fields["title"] = "Changed after the publish";
        var changed = await client.Client.SaveEntryAsync("article", id, fields, published.Read, Token);
        var discarded = await client.Client.DiscardDraftAsync("article", id, changed, Token);
        var unpublished = await client.Client.UnpublishEntryAsync("article", id, discarded, Token);

        published.Read.Entry.Status.ShouldBe("published");
        published.Read.EntityTag.ShouldBe($"\"{published.Read.Entry.Version}\"");
        published.Warnings.ShouldBeEmpty();
        changed.Entry!.Status.ShouldBe("modified");
        discarded.Entry!.Status.ShouldBe("published");
        discarded.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Published by the admin");
        discarded.EntityTag.ShouldBe($"\"{discarded.Entry.Version}\"");
        discarded.Entry.Version.ShouldBeGreaterThan(changed.Entry.Version);
        unpublished.Entry!.Status.ShouldBe("unpublished");
        unpublished.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Published by the admin");
        (await client.Client.GetEntryAsync("article", id, "en", Token)).EntityTag.ShouldBe(unpublished.EntityTag);
    }

    [Fact]
    public async Task APublishOnAStaleTagIsA412AndADiscardOfANeverPublishedEntryIsA409()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var created = await editor.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Read twice" }, editor.Client.StartAction("en"), Token);
        await editor.Client.SaveEntryAsync("article", created.Entry!.Id, new JsonObject { ["title"] = "Saved meanwhile" }, created, Token);

        var stale = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.PublishEntryAsync("article", created.Entry.Id, created, Token));
        var never = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.DiscardDraftAsync("article", created.Entry.Id, created with { EntityTag = "\"2\"" }, Token));

        stale.Problem.Status.ShouldBe(412);
        stale.Problem.Current!["title"]!.GetValue<string>().ShouldBe("Saved meanwhile");
        never.Problem.Status.ShouldBe(409);
    }

    // The locale is the read's: publishing French leaves English a draft.
    [Fact]
    public async Task EachActionIsInTheLocaleOfTheRead()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var english = await editor.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "English" }, editor.Client.StartAction("en"), Token);
        var id = english.Entry!.Id;
        var french = await editor.Client.SaveEntryAsync("article", id, new JsonObject { ["title"] = "Français" }, await editor.Client.GetEntryAsync("article", id, "fr", Token), Token);

        var published = await editor.Client.PublishEntryAsync("article", id, french, Token);

        (published.Read.Locale, published.Read.Entry!.Locale, published.Read.Entry.Status).ShouldBe(("fr", "fr", "published"));
        (await editor.Client.GetEntryAsync("article", id, "en", Token)).Entry!.Status.ShouldBe("draft");
    }

    [Fact]
    public async Task AReferenceToAnUnpublishedAuthorIsAWarningThatNamesItOrA422ForATypeThatBlocks()
    {
        await using var admin = new AdminHost(contentTypes: typeof(Review));
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var author = await editor.Client.CreateEntryAsync("author", new JsonObject { ["name"] = "Not yet published", ["slug"] = "not-yet-published" }, editor.Client.StartAction("en"), Token);
        var reference = author.Entry!.Id.ToString("D");
        var article = await editor.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Refers to a draft", ["author"] = reference }, editor.Client.StartAction("en"), Token);
        var review = await editor.Client.CreateEntryAsync("review", new JsonObject { ["title"] = "Blocked", ["author"] = reference }, editor.Client.StartAction("en"), Token);

        var warned = await editor.Client.PublishEntryAsync("article", article.Entry!.Id, article, Token);
        var blocked = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.PublishEntryAsync("review", review.Entry!.Id, review, Token));

        warned.Read.Entry!.Status.ShouldBe("published");
        var warning = warned.Warnings.ShouldHaveSingleItem();
        (warning.Path, warning.Code, warning.TargetType, warning.TargetId).ShouldBe(("author", PublishWarningCodes.UnpublishedReference, "author", author.Entry.Id));
        warning.Message.ShouldNotBeNullOrEmpty();
        blocked.Problem.Status.ShouldBe(422);
        blocked.Problem.Errors.ShouldHaveSingleItem().Path.ShouldBe("author");
    }

    [FluentCMS.Schema.Collection("review"), PublishPolicy(UnpublishedReferences = UnpublishedReferencePolicy.Block)]
    public sealed class Review : ContentItem
    {
        [Text]
        public string Title { get; set; } = "";

        [Relation]
        public Ref<Author> Author { get; set; }
    }
}
