using System.Text.Json;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class RichTextTests
{
    [Fact]
    public void EmptyIsAProseMirrorDocument()
    {
        using var document = JsonDocument.Parse(RichText.Empty.Json);

        document.RootElement.GetProperty("type").GetString().ShouldBe("doc");
        document.RootElement.GetProperty("content").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void EqualWhenJsonIsIdentical()
    {
        const string json = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}""";

        var a = new RichText(json);
        var b = new RichText(json);

        a.ShouldNotBeSameAs(b);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.Equals(RichText.Empty).ShouldBeFalse();
    }

    [Fact]
    public void EmptyEqualsAFreshDocumentWithTheSameJson()
    {
        new RichText(RichText.Empty.Json).Equals(RichText.Empty).ShouldBeTrue();
    }

    [Fact]
    public void RejectsNullJson()
    {
        Should.Throw<ArgumentNullException>(() => new RichText(null!));
    }
}
