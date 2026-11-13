using FluentCMS.Core.Content;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content;

public class VersionSnapshotTests
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddComponent("social", component => component.Field("person", "text", field => field.FormerNames("owner")))
        .AddComponent("seo", component => component
            .Field("pageTitle", "text", field => field.FormerNames("metaTitle"))
            .Field("social", "component", field => field.Setting(FieldSettings.Component, "social")))
        .AddComponent("address", component => component.Field("street", "text", field => field.FormerNames("line")))
        .AddCollection("article", type => type
            .Field("summary", "text", field => field.FormerNames("teaser", "subtitle"))
            .Field("title", "text")
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo").FormerNames("meta"))
            .Field("places", "component", field => field.Multiple().Setting(FieldSettings.Component, "address")))
        .Build();

    private static string Read(string snapshot) =>
        VersionSnapshot.Parse(Schema, Schema.FindContentType("article")!, snapshot).ToJsonString();

    [Fact]
    public void AFieldIsReadUnderItsFormerNameInItsPlace()
    {
        Read("""{"title":"T","subtitle":"S","seo":null}""").ShouldBe("""{"title":"T","summary":"S","seo":null}""");
    }

    [Fact]
    public void TheNameComesFirstAndThenTheFormerNamesInOrder()
    {
        Read("""{"summary":"now","teaser":"older","subtitle":"oldest"}""").ShouldBe("""{"summary":"now","teaser":"older","subtitle":"oldest"}""");
        Read("""{"subtitle":"oldest","teaser":"older"}""").ShouldBe("""{"subtitle":"oldest","summary":"older"}""");
    }

    [Fact]
    public void ANamePresentInTheSnapshotDecidesEvenWhenItHoldsNull()
    {
        Read("""{"summary":null,"subtitle":"S"}""").ShouldBe("""{"summary":null,"subtitle":"S"}""");
    }

    [Fact]
    public void TheFieldsOfASingleComponentAreReadUnderTheirFormerNamesToo()
    {
        Read("""{"meta":{"metaTitle":"M","social":{"owner":"O"}}}""").ShouldBe("""{"seo":{"pageTitle":"M","social":{"person":"O"}}}""");
    }

    [Fact]
    public void AComponentKeptInJsonKeepsTheNamesItWasStoredWith()
    {
        Read("""{"places":[{"line":"Main"}]}""").ShouldBe("""{"places":[{"line":"Main"}]}""");
    }

    [Fact]
    public void ASnapshotThatIsNotAnObjectHasNoFields()
    {
        Read("[]").ShouldBe("{}");
    }
}
