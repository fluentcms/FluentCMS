using FluentCMS.Media;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Media;

public class MediaRefTests
{
    private static Guid Id(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void RefIsEqualWhenIdsAreEqual()
    {
        var a = new MediaRef(Id(1));

        a.Equals(new MediaRef(Id(1))).ShouldBeTrue();
        (a == new MediaRef(Id(1))).ShouldBeTrue();
        (a != new MediaRef(Id(2))).ShouldBeTrue();
        a.GetHashCode().ShouldBe(new MediaRef(Id(1)).GetHashCode());
    }

    [Fact]
    public void RefWithExpressionCreatesNewValue()
    {
        var original = new MediaRef(Id(1));

        var changed = original with { Id = Id(2) };

        changed.Id.ShouldBe(Id(2));
        original.Id.ShouldBe(Id(1));
    }

    [Fact]
    public void DefaultRefIsEmpty()
    {
        default(MediaRef).IsEmpty.ShouldBeTrue();
        new MediaRef(Id(1)).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void NullableRefDistinguishesNoMediaFromEmptyReference()
    {
        MediaRef? none = null;
        MediaRef? empty = default(MediaRef);

        none.ShouldBeNull();
        empty.ShouldNotBeNull();
    }

    [Fact]
    public void ListsAreEqualWhenIdsMatchInOrder()
    {
        MediaRefList a = [new(Id(1)), new(Id(2))];
        MediaRefList b = [new(Id(1)), new(Id(2))];

        a.ShouldNotBeSameAs(b);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ListsAreNotEqualWhenOrderOrContentDiffers()
    {
        MediaRefList list = [new(Id(1)), new(Id(2))];

        list.Equals(new MediaRefList([new(Id(2)), new(Id(1))])).ShouldBeFalse();
        list.Equals(new MediaRefList([new(Id(1))])).ShouldBeFalse();
        list.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void EmptyListsAreEqual()
    {
        MediaRefList fromExpression = [];

        fromExpression.Equals(new MediaRefList()).ShouldBeTrue();
        fromExpression.Count.ShouldBe(0);
    }

    [Fact]
    public void ListKeepsTheOrderItWasGiven()
    {
        MediaRefList list = [new(Id(3)), new(Id(1))];

        list[0].ShouldBe(new MediaRef(Id(3)));
        list.Select(r => r.Id).ShouldBe([Id(3), Id(1)]);
    }

    [Fact]
    public void ListIsNotAffectedByLaterChangesToItsSource()
    {
        var source = new List<MediaRef> { new(Id(1)) };
        var list = new MediaRefList(source);

        source.Add(new MediaRef(Id(2)));

        list.Count.ShouldBe(1);
    }

    [Fact]
    public void ListRejectsANullSource()
    {
        Should.Throw<ArgumentNullException>(() => new MediaRefList(null!));
    }
}
