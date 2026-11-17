using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The calls of the edit form (A-13) on the Blog sample: a new entry from the defaults of the schema, created with a Location
// that names its route, read with its tag and saved whole with If-Match.
public sealed class FormTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheFormCreatesReadsAndSavesAnArticle(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var article = (await client.Metadata.GetSchemaAsync(Token)).Types.Single(type => type.Name == "article");
        var fields = ContentFormLayout.Defaults(article);
        fields["title"] = "Written in the form";

        var created = await client.Client.CreateEntryAsync("article", fields, client.Client.StartAction(), Token);
        var read = await client.Client.GetEntryAsync("article", created.Entry!.Id, null, Token);
        var changed = read.Entry!.Fields.DeepClone().AsObject();
        changed["title"] = "Saved by the form";
        var saved = await client.Client.SaveEntryAsync("article", created.Entry.Id, changed, read, Token);

        ContentFormLayout.Build(article).SelectMany(tab => tab.Fields).Select(field => field.Name).ShouldContain("title");
        var location = created.Location.ShouldNotBeNull();
        location.OriginalString.Split('?')[0].ShouldEndWith($"/{ContentRoutes.ManagementPrefix}{ContentRoutes.Entry("article", created.Entry.Id)}");
        read.EntityTag.ShouldBe(created.EntityTag);
        read.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Written in the form");
        saved.Entry!.Version.ShouldBe(read.Entry.Version + 1);
        saved.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Saved by the form");
        (await client.Client.GetEntryAsync("article", created.Entry.Id, null, Token)).EntityTag.ShouldBe(saved.EntityTag);
    }

    // The behaviours of A-49: /me/access names the fields the form lets the caller change, a stale save answers a 412 whose
    // current entry the conflict dialog reads, and keep mine saves on its version. A field the caller may read but not change
    // is sent back as it was read, which the server takes as no write of it.
    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheFormReadsTheFieldsItMayChangeAndKeepsMineOnTheVersionOfAConflict(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var article = (await client.Metadata.GetSchemaAsync(Token)).Types.Single(type => type.Name == "article");
        var access = (await client.Client.GetMyAccessAsync(null, Token)).Types["article"];

        (access.Create, access.Update).ShouldBe((true, true));
        access.CreatableFields.ShouldContain("title");
        access.UpdatableFields.ShouldContain("title");
        access.UpdatableFields.ShouldBeSubsetOf(article.Fields.Select(field => field.Name));
        var fields = ContentFormLayout.Defaults(article);
        fields["title"] = "Read by both";
        var created = await client.Client.CreateEntryAsync("article", fields, client.Client.StartAction(), Token);
        var mine = await client.Client.GetEntryAsync("article", created.Entry!.Id, null, Token);
        var theirs = mine.Entry!.Fields.DeepClone().AsObject();
        theirs["title"] = "Saved by them";
        var saved = await client.Client.SaveEntryAsync("article", created.Entry.Id, theirs, mine, Token);

        var changed = mine.Entry.Fields.DeepClone().AsObject();
        changed["title"] = "Saved by me";
        var conflict = await Should.ThrowAsync<ApiProblemException>(() => client.Client.SaveEntryAsync("article", created.Entry.Id, changed, mine, Token));

        conflict.Problem.Status.ShouldBe(412);
        var current = ManagementClient.CurrentOf(conflict.Problem, mine).ShouldNotBeNull();
        current.EntityTag.ShouldBe(saved.EntityTag);
        current.Entry!.Fields["title"]!.GetValue<string>().ShouldBe("Saved by them");
        var kept = await client.Client.SaveEntryAsync("article", created.Entry.Id, changed, current, Token);
        kept.Entry!.Version.ShouldBe(saved.Entry!.Version + 1);
        kept.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Saved by me");
    }
}
