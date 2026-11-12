using System.Text.Json;
using FluentCMS.Generators.Emit;
using Shouldly;
using Xunit;

namespace FluentCMS.Generators.Tests;

public sealed class EmitterTests
{
    private static FieldDescription Field(string name, string fieldType, bool multiple = false, string? component = null, params string[] blocks) =>
        new(name, fieldType, multiple, component, blocks.ToEquatableArray());

    private static TypeDescription Type(string name, BuilderKind kind, params FieldDescription[] fields) =>
        new(name, kind, "Client", fields.ToEquatableArray());

    private static EquatableArray<FieldDescription> Fields(params FieldDescription[] fields) => fields.ToEquatableArray();

    [Fact]
    public void TheNamespaceOfTheFieldTypesIsAnOption()
    {
        var result = FieldsEmitter.Emit([Type("post", BuilderKind.Collection, Field("title", "text"))], new EmitOptions("FluentCMS.Client.Fields"));

        var text = result.Sources.ShouldHaveSingleItem().Text;
        text.ShouldContain("public static global::FluentCMS.Client.Fields.TextField Title { get; } = new global::FluentCMS.Client.Fields.TextField(\"title\");");
        text.ShouldNotContain("FluentCMS.Content");
    }

    [Fact]
    public void ASingletonAndATypeInTheGlobalNamespaceGetBuilders()
    {
        var result = FieldsEmitter.Emit([new TypeDescription("site-settings", BuilderKind.Singleton, "", Fields(Field("title", "text")))], EmitOptions.Sdk);

        var source = result.Sources.ShouldHaveSingleItem();
        source.HintName.ShouldBe("SiteSettingsFields.g.cs");
        source.Text.ShouldNotContain("namespace");
        source.Text.ShouldContain("the singleton <c>site-settings</c>");
    }

    [Fact]
    public void TwoTypesWithOneBuilderNameKeepTheFirst()
    {
        var result = FieldsEmitter.Emit([Type("a1", BuilderKind.Collection), Type("a-1", BuilderKind.Collection), Type("a1", BuilderKind.Component)], EmitOptions.Sdk);

        result.Sources.Select(source => source.HintName).ShouldBe(["Client.A1Fields.g.cs", "Client.A1ComponentFields.g.cs"]);
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("FCMS0100");
        diagnostic.TypeName.ShouldBe("a1");
    }

    [Fact]
    public void ATypeWhoseNameGivesNoClassNameIsReported()
    {
        var result = FieldsEmitter.Emit([Type("Post", BuilderKind.Collection)], EmitOptions.Sdk);

        result.Sources.ShouldBeEmpty();
        result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("FCMS0100");
    }

    [Fact]
    public void AFieldTypeWithNoBuilderIsReportedAndARepeatedComponentIsLeftOut()
    {
        var result = FieldsEmitter.Emit(
        [
            Type("post", BuilderKind.Collection, Field("location", "geo"), Field("addresses", "component", multiple: true, component: "address")),
            Type("address", BuilderKind.Component, Field("city", "text")),
        ], EmitOptions.Sdk);

        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        (diagnostic.Code, diagnostic.TypeName, diagnostic.FieldName).ShouldBe(("FCMS0101", "post", "location"));
        result.Sources.Single(source => source.HintName == "Client.PostFields.g.cs").Text.ShouldNotContain("Addresses");
    }

    [Fact]
    public void ABlockTypeWithNoBuilderKeepsItsPresenceHelper()
    {
        var result = FieldsEmitter.Emit([Type("page", BuilderKind.Collection, Field("body", "blocks", blocks: ["banner", "card"])), Type("card", BuilderKind.Block)], EmitOptions.Sdk);

        var text = result.Sources.Single(source => source.HintName == "Client.PageFields.g.cs").Text;
        text.ShouldContain("public global::FluentCMS.Content.FieldFilter HasBanner() => HasBlock(\"banner\");");
        text.ShouldNotContain("BannerBlockFields");
        text.ShouldContain("public global::Client.CardBlockFields Card => new global::Client.CardBlockFields(Path);");
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        (diagnostic.Code, diagnostic.FieldName).ShouldBe(("FCMS0103", "body"));
    }

    [Fact]
    public void ABlockTypeWithAHyphenKeepsOnlyItsPresenceHelper()
    {
        var result = FieldsEmitter.Emit([Type("page", BuilderKind.Collection, Field("body", "blocks", blocks: ["call-to-action"])), Type("call-to-action", BuilderKind.Block, Field("label", "text"))], EmitOptions.Sdk);

        var source = result.Sources.ShouldHaveSingleItem();
        source.Text.ShouldContain("public global::FluentCMS.Content.FieldFilter HasCallToAction() => HasBlock(\"call-to-action\");");
        source.Text.ShouldNotContain("CallToActionBlockFields");
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        (diagnostic.Code, diagnostic.TypeName, diagnostic.Kind).ShouldBe(("FCMS0104", "call-to-action", BuilderKind.Block));
    }

    [Fact]
    public void AMemberNameThatClashesWithObjectOrTheBuilderIsReported()
    {
        var result = FieldsEmitter.Emit(
        [
            Type("post", BuilderKind.Collection, Field("toString", "text"), Field("postFields", "text"), Field("body", "blocks", blocks: ["path"])),
            Type("path", BuilderKind.Block),
        ], EmitOptions.Sdk);

        result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.FieldName)).ShouldBe([("FCMS0102", "toString"), ("FCMS0102", "postFields"), ("FCMS0102", "body")]);
    }

    [Fact]
    public void TheOutputDoesNotDependOnTheOrderOfTheTypes()
    {
        TypeDescription[] types = [Type("b", BuilderKind.Collection, Field("x", "text")), Type("a", BuilderKind.Block), Type("c", BuilderKind.Component)];

        FieldsEmitter.Emit(types, EmitOptions.Sdk).ShouldBe(FieldsEmitter.Emit(types.Reverse(), EmitOptions.Sdk));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("URLSlug")]
    [InlineData("PublishedOn")]
    [InlineData("ID")]
    [InlineData("IOStream")]
    [InlineData("Seo2Title")]
    [InlineData("A")]
    [InlineData("x")]
    public void TheNamesAreThoseOfTheAttributeReader(string name)
    {
        SchemaNames.ToCamelCase(name).ShouldBe(JsonNamingPolicy.CamelCase.ConvertName(name));
        SchemaNames.ToKebabCase(name).ShouldBe(JsonNamingPolicy.KebabCaseLower.ConvertName(name));
    }

    [Theory]
    [InlineData("HeroBlock")]
    [InlineData("FAQPage")]
    [InlineData("SHA512Hash")]
    [InlineData("XMLReader")]
    [InlineData("Page2")]
    [InlineData("site_settings")]
    public void TheTypeNamesAreThoseOfTheAttributeReader(string name) =>
        SchemaNames.ToKebabCase(name).ShouldBe(JsonNamingPolicy.KebabCaseLower.ConvertName(name));
}
