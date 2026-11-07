using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class FieldReferenceTests
{
    [Fact]
    public void ASettingThatNamesASiblingBuilds()
    {
        var model = ReferenceFieldType.Builder()
            .AddCollection("article", type => type.Field("title", "text").Field("summary", ReferenceFieldType.TypeName, field => field.Setting(ReferenceFieldType.From, "title")))
            .AddComponent("card", type => type.Field("label", "text").Field("short", ReferenceFieldType.TypeName, field => field.Setting(ReferenceFieldType.From, "label")))
            .Build();

        model.FindContentType("article")!.Fields.Single(field => field.Name == "summary").Settings[ReferenceFieldType.From].ShouldBe("title");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("summary")]
    public void ASettingThatNamesNoSiblingIsFCMS1520(string named)
    {
        var builder = ReferenceFieldType.Builder()
            .AddCollection("article", type => type.Field("title", "text").Field("summary", ReferenceFieldType.TypeName, field => field.Setting(ReferenceFieldType.From, named)));

        var error = Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1520");
        error.Message.ShouldBe($"setting 'from' of excerpt field article.summary names '{named}', which is not another field of collection 'article'");
    }

    // A field of the content type is not a sibling of a field of its component.
    [Fact]
    public void ASiblingIsAFieldOfTheSameTypeComponentOrBlock()
    {
        var builder = ReferenceFieldType.Builder()
            .AddCollection("article", type => type.Field("title", "text").Field("card", "component", field => field.Setting(FieldSettings.Component, "card")))
            .AddComponent("card", type => type.Field("short", ReferenceFieldType.TypeName, field => field.Setting(ReferenceFieldType.From, "title")));

        Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS1520");
    }

    [Fact]
    public void ASettingThatIsNotANameIsReportedOnceAsTheWrongKind()
    {
        var builder = ReferenceFieldType.Builder()
            .AddCollection("article", type => type.Field("title", "text").Field("summary", ReferenceFieldType.TypeName, field => field.Setting(ReferenceFieldType.From, 3L)));

        var error = Should.Throw<SchemaValidationException>(() => builder.Build()).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe("FCMS1211");
        error.Message.ShouldBe("setting 'from' of excerpt field article.summary must be the name of another field");
    }
}
