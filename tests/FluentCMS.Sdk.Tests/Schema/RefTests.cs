using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class RefTests
{
    private sealed class Author : ContentItem;

    private sealed class Tag : ContentItem;

    private static Guid Id(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void RefIsEqualWhenIdsAreEqual()
    {
        var a = new Ref<Author>(Id(1));

        a.Equals(new Ref<Author>(Id(1))).ShouldBeTrue();
        (a == new Ref<Author>(Id(1))).ShouldBeTrue();
        a.GetHashCode().ShouldBe(new Ref<Author>(Id(1)).GetHashCode());
        (a != new Ref<Author>(Id(2))).ShouldBeTrue();
    }

    [Fact]
    public void RefWithExpressionCreatesNewValue()
    {
        var original = new Ref<Author>(Id(1));

        var changed = original with { Id = Id(2) };

        changed.Id.ShouldBe(Id(2));
        original.Id.ShouldBe(Id(1));
    }

    [Fact]
    public void DefaultRefIsEmpty()
    {
        default(Ref<Author>).IsEmpty.ShouldBeTrue();
        new Ref<Author>(Id(1)).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void RefWorksAsADictionaryKey()
    {
        var set = new HashSet<Ref<Tag>> { new(Id(1)), new(Id(2)), new(Id(1)) };

        set.Count.ShouldBe(2);
    }

    [Fact]
    public void ListsAreEqualWhenIdsMatchInOrder()
    {
        RefList<Tag> a = [new(Id(1)), new(Id(2))];
        RefList<Tag> b = [new(Id(1)), new(Id(2))];

        a.ShouldNotBeSameAs(b);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ListsAreNotEqualWhenOrderOrContentDiffers()
    {
        RefList<Tag> list = [new(Id(1)), new(Id(2))];

        list.Equals(new RefList<Tag>([new(Id(2)), new(Id(1))])).ShouldBeFalse();
        list.Equals(new RefList<Tag>([new(Id(1))])).ShouldBeFalse();
        list.Equals(new RefList<Tag>([new(Id(1)), new(Id(3))])).ShouldBeFalse();
        (list != new RefList<Tag>()).ShouldBeTrue();
        list.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void EmptyListsAreEqual()
    {
        RefList<Tag> fromExpression = [];

        fromExpression.Equals(new RefList<Tag>()).ShouldBeTrue();
        fromExpression.Count.ShouldBe(0);
    }

    [Fact]
    public void ListKeepsTheOrderItWasGiven()
    {
        RefList<Tag> list = [new(Id(3)), new(Id(1)), new(Id(2))];

        list.Count.ShouldBe(3);
        list[0].ShouldBe(new Ref<Tag>(Id(3)));
        list.Select(r => r.Id).ShouldBe([Id(3), Id(1), Id(2)]);
    }

    [Fact]
    public void ListIsNotAffectedByLaterChangesToItsSource()
    {
        var source = new List<Ref<Tag>> { new(Id(1)) };
        var list = new RefList<Tag>(source);

        source.Add(new Ref<Tag>(Id(2)));

        list.Count.ShouldBe(1);
    }

    [Fact]
    public void ListWithExpressionSharesNoStateButStaysEqual()
    {
        RefList<Tag> original = [new(Id(1))];

        var copy = original with { };

        copy.ShouldNotBeSameAs(original);
        copy.Equals(original).ShouldBeTrue();
    }

    [Fact]
    public void ListRejectsANullSource()
    {
        Should.Throw<ArgumentNullException>(() => new RefList<Tag>(null!));
    }

    [Fact]
    public void ListCanBeAPropertyInitialisedWithAnEmptyCollectionExpression()
    {
        var entry = new Entry();

        entry.Tags.Count.ShouldBe(0);
        entry.Author.IsEmpty.ShouldBeTrue();
    }

    private sealed class Entry : ContentItem
    {
        public Ref<Author> Author { get; set; }

        public RefList<Tag> Tags { get; set; } = [];
    }
}
