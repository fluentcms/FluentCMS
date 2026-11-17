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
}
