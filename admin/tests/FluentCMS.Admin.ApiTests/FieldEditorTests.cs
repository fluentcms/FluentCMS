using System.Globalization;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Localization;
using FluentCMS.Core.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The field editors and cells of A-14 on the Blog sample: the settings they read are in the schema as they read them, every
// field of an article outside the types of later issues has an editor, and the values they write are taken and read back.
public sealed class FieldEditorTests
{
    // Relation (A-15), media (A-20), rich text (A-24), component and blocks (A-25, A-26).
    private static readonly HashSet<string> Later = ["relation", "media", "richText", "component", "blocks"];

    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheArticleIsEditedWithTheCoreEditorsAndTheirValuesAreTaken(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var article = (await client.Metadata.GetSchemaAsync(Token)).Types.Single(type => type.Name == "article");
        var editors = new ServiceCollection().AddFieldEditors().BuildServiceProvider().GetRequiredService<FieldEditorRegistry>();

        article.Fields.Where(field => !Later.Contains(field.Type) && editors.Find(field.Type) is null).ShouldBeEmpty();
        var category = article.Fields.Single(field => field.Name == "category");
        FieldValues.Options(category).ShouldBe(["news", "opinion", "video"]);
        FieldValues.Text(article.Fields.Single(field => field.Name == "slug").Settings["source"]).ShouldBe("title");

        var fields = ContentFormLayout.Defaults(article);
        fields["title"] = "Crème brûlée, again!";
        fields["slug"] = FieldValues.FromText(Slugs.Slugify("Crème brûlée, again!"));
        fields["category"] = FieldValues.FromText("video");
        fields["videoUrl"] = FieldValues.FromText("https://example.com/watch");
        fields["publishedOn"] = FieldValues.FromMoment(new DateTimeOffset(2026, 11, 16, 6, 30, 0, TimeSpan.FromHours(9)));
        var created = await client.Client.CreateEntryAsync("article", fields, client.Client.StartAction(), Token);
        var read = (await client.Client.GetEntryAsync("article", created.Entry!.Id, null, Token)).Entry!.Fields;

        read["slug"]!.GetValue<string>().ShouldBe("creme-brulee-again");
        read["category"]!.GetValue<string>().ShouldBe("video");
        FieldValues.Moment(read["publishedOn"]).ShouldBe(new DateTimeOffset(2026, 11, 15, 21, 30, 0, TimeSpan.Zero));
        FieldValues.Text(read["videoUrl"]).ShouldBe("https://example.com/watch");
    }

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task TheFormatsAreReadFromThePreferencesAsTheShellReadsThem(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var account = client.Client;
        var current = await account.GetMyPreferencesAsync(Token);

        var saved = await account.SetMyPreferencesAsync(current with { TimeZone = "Asia/Tokyo", DateFormat = "de-DE", NumberFormat = "fr-FR" }, Token);
        var read = await account.GetMyPreferencesAsync(Token);

        read.ShouldBe(saved);
        var formats = new FormatService(CultureInfo.InvariantCulture, TimeZoneInfo.Utc);
        formats.Apply(read.DateFormat, read.NumberFormat, read.TimeZone);
        formats.Culture.Name.ShouldBe("de-DE");
        formats.NumberCulture.Name.ShouldBe("fr-FR");
        formats.TimeZone.Id.ShouldBe("Asia/Tokyo");
    }
}
