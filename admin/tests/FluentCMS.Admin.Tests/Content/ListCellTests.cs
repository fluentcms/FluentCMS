using System.Globalization;
using System.Text.Json.Nodes;
using Bunit;
using Bunit.Rendering;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// The cell of each core field type under both pseudo-cultures, in the user's formats.
public sealed class ListCellTests : AdminTestContext
{
    public static TheoryData<string> Cultures => [PseudoCulture.LeftToRight, PseudoCulture.RightToLeft];

    private FormatService Formats => Services.GetRequiredService<FormatService>();

    [Fact]
    public void EveryCoreFieldTypeHasACellOfItsOwn()
    {
        Begin(PseudoCulture.LeftToRight);
        var cells = Services.GetRequiredService<ListCellRegistry>();

        foreach (var type in FieldEditorTests.CoreTypes)
        {
            cells.CellFor(type).ShouldNotBe(typeof(TextListCell), type);
        }

        cells.CellFor("relation").ShouldBe(typeof(RelationListCell));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ANumberIsInTheUsersNumberFormat(string pseudo)
    {
        Begin(pseudo);

        Cell("decimal", JsonValue.Create(1234567.5m)).Find("span").TextContent.ShouldBe(1234567.5m.ToString("#,0.#", Formats.NumberCulture));
        Cell("integer", JsonValue.Create(42)).Find("span").TextContent.ShouldBe("42");
        Cell("decimal", null).Find("span").TextContent.ShouldBeEmpty();
    }

    [Fact]
    public void NumbersFollowTheNumberFormatOfThePreferences()
    {
        Begin(PseudoCulture.LeftToRight);
        Formats.Apply(null, "de-DE", null);

        Cell("decimal", JsonValue.Create(1234.5m)).Find("span").TextContent.ShouldBe("1.234,5");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ABooleanIsAWord(string pseudo)
    {
        Begin(pseudo);

        Cell("boolean", JsonValue.Create(true)).Find("span").TextContent.ShouldBe(Text("Content.Cell.Yes"));
        Cell("boolean", JsonValue.Create(false)).Find("span").TextContent.ShouldBe(Text("Content.Cell.No"));
        Cell("boolean", null).Find("span").TextContent.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateIsInTheUsersFormatAndTheSameDayInEveryTimeZone(string pseudo)
    {
        Begin(pseudo);
        Formats.Apply(null, null, "Pacific/Honolulu");

        var time = Cell("date", JsonValue.Create("2026-11-15")).Find("time");

        time.GetAttribute("datetime").ShouldBe("2026-11-15");
        time.TextContent.ShouldBe(new DateOnly(2026, 11, 15).ToString("d", Formats.Culture));
        Cell("date", null).FindAll("time").ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateTimeIsInTheUsersFormatAndTimeZone(string pseudo)
    {
        Begin(pseudo);
        Formats.Apply(null, null, "Asia/Tokyo");

        var time = Cell("dateTime", JsonValue.Create("2026-11-15T20:00:00Z")).Find("time");

        time.TextContent.ShouldBe(new DateTime(2026, 11, 16, 5, 0, 0).ToString("g", Formats.Culture));
        Cell("dateTime", null).FindAll("time").ShouldBeEmpty();
    }

    [Fact]
    public void ADateTimeFollowsTheDateFormatOfThePreferences()
    {
        Begin(PseudoCulture.LeftToRight);
        Formats.Apply("de-DE", null, "Asia/Tokyo");

        Cell("dateTime", JsonValue.Create("2026-11-15T20:00:00Z")).Find("time").TextContent.ShouldBe("16.11.2026 05:00");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ASelectShowsItsValues(string pseudo)
    {
        Begin(pseudo);

        Cell("select", JsonValue.Create("news")).Find("span").TextContent.ShouldBe("news");
        Cell("select", new JsonArray("a", "b")).Find("span").TextContent.ShouldBe("a, b");
        Cell("select", null).Find("span").TextContent.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextIsInTheLanguageAndDirectionOfTheEntryAndAnEmailOrUrlIsLeftToRight(string pseudo)
    {
        Begin(pseudo);

        var text = Cell("text", JsonValue.Create("مرحبا"), "ar").Find("span");
        (text.GetAttribute("lang"), text.GetAttribute("dir")).ShouldBe(("ar", "rtl"));
        Cell("longText", JsonValue.Create("long"), "en").Find("span").ClassList.ShouldContain("list-cell-long");

        var slug = Cell("slug", JsonValue.Create("مرحبا"), "ar").Find("span");
        (slug.GetAttribute("lang"), slug.GetAttribute("dir")).ShouldBe(("ar", "rtl"));
        Cell("text", JsonValue.Create("hello"), "en").Find("span").GetAttribute("dir").ShouldBe("ltr");

        foreach (var type in new[] { "email", "url" })
        {
            var address = Cell(type, JsonValue.Create("https://example.com"), "ar").Find("span");
            (address.HasAttribute("lang"), address.GetAttribute("dir")).ShouldBe((false, "ltr"), type);
        }

        foreach (var type in new[] { "text", "longText", "slug", "email", "url" })
        {
            Cell(type, null, "en").Find("span").TextContent.ShouldBeEmpty(type);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void JsonIsOnOneLine(string pseudo)
    {
        Begin(pseudo);

        Cell("json", new JsonObject { ["a"] = new JsonArray(1, 2) }).Find("code").TextContent.ShouldBe("""{"a":[1,2]}""");
        Cell("json", null).FindAll("code").ShouldBeEmpty();
    }

    private void Begin(string pseudo)
    {
        UseInterfaceCulture(pseudo);
        Start();
    }

    private IRenderedComponent<ContainerFragment> Cell(string type, JsonNode? value, string? locale = null)
    {
        var field = new SchemaFieldDescription { Name = "field", Type = type, Validation = new FieldValidationDescription() };
        var entry = new ContentEntry { Id = Guid.CreateVersion7(), Locale = locale, Fields = new JsonObject { ["field"] = value?.DeepClone() } };
        var cell = Services.GetRequiredService<ListCellRegistry>().CellFor(type);
        return Render(builder =>
        {
            builder.OpenComponent(0, cell);
            builder.AddComponentParameter(1, nameof(ListCellBase.Field), field);
            builder.AddComponentParameter(2, nameof(ListCellBase.Entry), entry);
            builder.CloseComponent();
        });
    }
}
