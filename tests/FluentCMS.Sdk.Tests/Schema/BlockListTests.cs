using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class BlockListTests
{
    private sealed record HeroBlock(string Heading);

    private sealed record QuoteBlock(string Text);

    private sealed class PlainBlock;

    [Fact]
    public void EqualWhenBlocksAreEqualInOrder()
    {
        BlockList a = [new HeroBlock("Hi"), new QuoteBlock("Q")];
        BlockList b = [new HeroBlock("Hi"), new QuoteBlock("Q")];

        a.ShouldNotBeSameAs(b);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void NotEqualWhenOrderOrContentDiffers()
    {
        BlockList list = [new HeroBlock("Hi"), new QuoteBlock("Q")];

        list.Equals(new BlockList([new QuoteBlock("Q"), new HeroBlock("Hi")])).ShouldBeFalse();
        list.Equals(new BlockList([new HeroBlock("Hi")])).ShouldBeFalse();
        list.Equals(new BlockList([new HeroBlock("Other"), new QuoteBlock("Q")])).ShouldBeFalse();
        list.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void BlocksWithoutValueEqualityAreComparedByReference()
    {
        var shared = new PlainBlock();

        new BlockList([shared]).Equals(new BlockList([shared])).ShouldBeTrue();
        new BlockList([new PlainBlock()]).Equals(new BlockList([new PlainBlock()])).ShouldBeFalse();
    }

    [Fact]
    public void KeepsTheOrderAndTypesItWasGiven()
    {
        BlockList list = [new QuoteBlock("Q"), new HeroBlock("Hi")];

        list.Count.ShouldBe(2);
        list[0].ShouldBeOfType<QuoteBlock>();
        list.OfType<HeroBlock>().Single().Heading.ShouldBe("Hi");
    }

    [Fact]
    public void EmptyListsAreEqual()
    {
        BlockList fromExpression = [];

        fromExpression.Equals(new BlockList()).ShouldBeTrue();
        fromExpression.Count.ShouldBe(0);
    }

    [Fact]
    public void IsNotAffectedByLaterChangesToItsSource()
    {
        var source = new List<object> { new HeroBlock("Hi") };
        var list = new BlockList(source);

        source.Add(new QuoteBlock("Q"));

        list.Count.ShouldBe(1);
    }

    [Fact]
    public void RejectsNullBlocksAndNullSources()
    {
        Should.Throw<ArgumentNullException>(() => new BlockList(null!));
        Should.Throw<ArgumentNullException>(() => new BlockList([new HeroBlock("Hi"), null!]));
        Should.Throw<ArgumentNullException>(() => BlockList.Create([null!]));
    }
}
