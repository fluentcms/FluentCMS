using FluentCMS.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class ContentDocumentTests
{
    [Fact]
    public void ValuesKeepTheOrderTheyWereAddedIn()
    {
        var document = new ContentDocument { ["b"] = 1, ["a"] = 2 };
        document["c"] = 3;
        document["b"] = 4;

        document.Keys.ShouldBe(["b", "a", "c"]);
        document["b"].ShouldBe(4);
        document.Count.ShouldBe(3);
    }

    [Fact]
    public void NamesAreCaseSensitive()
    {
        var document = new ContentDocument { ["title"] = "a" };

        document.ContainsKey("Title").ShouldBeFalse();
        document.TryGetValue("title", out var value).ShouldBeTrue();
        value.ShouldBe("a");
    }

    [Fact]
    public void GetReturnsTheValueAsItsOwnTypeOrTheDefault()
    {
        var document = new ContentDocument { ["title"] = "a", ["nothing"] = null };

        document.Get<string>("title").ShouldBe("a");
        document.Get<string>("nothing").ShouldBeNull();
        document.Get<string>("missing").ShouldBeNull();
        document.Get<int>("missing").ShouldBe(0);
        document.TryGet<string>("title", out var title).ShouldBeTrue();
        title.ShouldBe("a");
        document.TryGet<string>("nothing", out _).ShouldBeFalse();
    }

    [Fact]
    public void ANumberConvertsToAnotherNumericTypeWhenItFits()
    {
        var document = new ContentDocument { ["count"] = 12L, ["price"] = 2.0, ["big"] = long.MaxValue, ["half"] = 1.5m };

        document.Get<int>("count").ShouldBe(12);
        document.Get<int?>("count").ShouldBe(12);
        document.Get<decimal>("count").ShouldBe(12m);
        document.Get<int>("price").ShouldBe(2);
        Should.Throw<InvalidCastException>(() => document.Get<int>("big"));
        Should.Throw<InvalidCastException>(() => document.Get<int>("half"));
    }

    [Fact]
    public void NothingElseConverts()
    {
        var document = new ContentDocument { ["text"] = "12", ["count"] = 12, ["flag"] = true };

        Should.Throw<InvalidCastException>(() => document.Get<int>("text"));
        Should.Throw<InvalidCastException>(() => document.Get<string>("count"));
        Should.Throw<InvalidCastException>(() => document.Get<int>("flag"));
        Should.Throw<InvalidCastException>(() => document.Get<bool>("count"));
        Should.Throw<InvalidCastException>(() => document.TryGet<int>("text", out _));
    }
}
