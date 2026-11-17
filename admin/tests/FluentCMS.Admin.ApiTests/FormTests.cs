using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.Extensions.DependencyInjection;
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

    // A role that may change only the title of an article: /me/access lists only it, the form sends the whole document back
    // with the other fields as read, which the server takes as no write of them, and a changed one is refused.
    [Fact]
    public async Task AFieldTheCallerMayNotChangeIsSentBackAsReadAndIsNoWrite()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var article = (await owner.Metadata.GetSchemaAsync(Token)).Types.Single(type => type.Name == "article");
        var fields = ContentFormLayout.Defaults(article);
        (fields["title"], fields["slug"]) = ("Only the title", "only-the-title");
        var created = await owner.Client.CreateEntryAsync("article", fields, owner.Client.StartAction(), Token);
        var role = await admin.Host.CreateRoleAsync("Title editor", ["content.article.read", CorePermissions.SchemaRead], Token);
        await using (var scope = admin.Host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                RoleId = role,
                Permission = "content.article.update",
                Constraints = new PermissionConstraints { Fields = ["title"] }.ToJson(),
            });
            await context.SaveChangesAsync(Token);
        }

        using var client = await admin.SignInAsync("Title editor");
        var access = (await client.Client.GetMyAccessAsync(null, Token)).Types["article"];
        var read = await client.Client.GetEntryAsync("article", created.Entry!.Id, null, Token);
        var changed = read.Entry!.Fields.DeepClone().AsObject();
        changed["title"] = "Changed by the title editor";
        var saved = await client.Client.SaveEntryAsync("article", created.Entry.Id, changed, read, Token);
        var other = saved.Entry!.Fields.DeepClone().AsObject();
        other["slug"] = "not-yours";

        access.Update.ShouldBeTrue();
        access.UpdatableFields.ShouldBe(["title"]);
        saved.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Changed by the title editor");
        saved.Entry.Fields["slug"]!.GetValue<string>().ShouldBe("only-the-title");
        var refused = await Should.ThrowAsync<ApiProblemException>(() => client.Client.SaveEntryAsync("article", created.Entry.Id, other, saved, Token));
        refused.Problem.Status.ShouldBeOneOf(403, 422);
    }

    // The Blog's videoUrl shows for a video article only; the form reads its visibleWhen as the server does.
    [Fact]
    public async Task TheFormReadsTheConditionsOfTheBlogAsTheServerDoes()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.EditorName);
        var article = (await client.Metadata.GetSchemaAsync(Token)).Types.Single(type => type.Name == "article");
        var videoUrl = article.Fields.Single(field => field.Name == "videoUrl");

        FormConditions.Shows(videoUrl, new() { ["category"] = "video" }).ShouldBeTrue();
        FormConditions.Shows(videoUrl, new() { ["category"] = "news" }).ShouldBeFalse();
        FormConditions.Requires(videoUrl, new() { ["category"] = "video" }).ShouldBeTrue();
        FormConditions.Requires(videoUrl, new() { ["category"] = "news" }).ShouldBeFalse();
    }
}
