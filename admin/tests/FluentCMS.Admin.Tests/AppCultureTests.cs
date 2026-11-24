using System.Globalization;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests;

// The user's formats as the app's current culture (spec 4.17): the culture of dates with the number format of the culture of
// numbers, which every text formatted with the current culture follows, as the count of a list, the limit in a validation
// message and a number in a text cell. They set the process's default culture, as the browser's app does, so they run alone.
[Collection(nameof(ProcessCulture))]
public sealed class AppCultureTests : AdminTestContext
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public AppCultureTests()
    {
        UseNeutralResources();
        UseAppCulture();
    }

    private FormatService Formats => Services.GetRequiredService<FormatService>();

    [Fact]
    public void TheCultureOfDatesWithTheNumbersOfTheCultureOfNumbersBecomesTheCurrentCulture()
    {
        Start();
        var browser = Formats.Culture;
        try
        {
            Formats.Apply("de-DE", "fr-FR", null);

            var current = CultureInfo.DefaultThreadCurrentCulture.ShouldNotBeNull();
            current.Name.ShouldBe("de-DE");
            current.DateTimeFormat.ShortDatePattern.ShouldBe(German.DateTimeFormat.ShortDatePattern);
            1234567.5m.ToString("N1", current).ShouldBe(1234567.5m.ToString("N1", French));
            CultureInfo.CurrentCulture.ShouldBeSameAs(current);

            Formats.Apply(null, null, null);

            CultureInfo.DefaultThreadCurrentCulture.ShouldBeSameAs(browser);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = null;
        }
    }

    [Fact]
    public void TheCountOfAListFollowsTheFormatOfNumbers()
    {
        Management.SetTypes(MainSite, Type("article", fields: [Field("title")]));
        Management.Entries["article"] = [new JsonObject { ["id"] = "0193a1b2-0000-7000-8000-000000000001", ["status"] = "draft", ["locale"] = "en", ["version"] = 1, ["title"] = "Hello" }];
        Management.Total = 1234567;
        Management.Preferences["numberFormat"] = "de-DE";
        try
        {
            Start();
            Services.GetRequiredService<NavigationManager>().NavigateTo("content/article");
            var app = Render<AdminApp>();

            var total = Text("Content.List.Total", 1234567L.ToString("N0", German));
            app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain(total));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = null;
        }
    }

    [Fact]
    public void TheLimitInAValidationMessageFollowsTheFormatOfNumbers()
    {
        Start();
        try
        {
            Formats.Apply(null, "fr-FR", null);
            var field = new SchemaFieldDescription { Name = "price", Type = "decimal", Validation = new FieldValidationDescription { Maximum = 1234.5m } };

            var errors = FieldRules.Check(field, JsonValue.Create(5000m), Services.GetRequiredService<IStringLocalizer<AdminResources>>());

            errors.ShouldHaveSingleItem().Message.ShouldBe(Text("Content.Form.Rule.Maximum", 1234.5m.ToString("G29", French)));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = null;
        }
    }

    [Fact]
    public void ANumberInATextCellFollowsTheFormatOfNumbers()
    {
        Start();
        try
        {
            Formats.Apply(null, "fr-FR", null);
            var field = new SchemaFieldDescription { Name = "score", Type = "score", Validation = new FieldValidationDescription() };
            var entry = new ContentEntry { Id = Guid.CreateVersion7(), Fields = new JsonObject { ["score"] = 1234.5m } };

            var cell = Render<TextListCell>(parameters => parameters.Add(cell => cell.Field, field).Add(cell => cell.Entry, entry));

            cell.Find("span").TextContent.ShouldBe(1234.5m.ToString(French));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = null;
        }
    }
}
