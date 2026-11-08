using FluentCMS.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class ContentQueryTests
{
    private static readonly FieldFilter Title = new("title", FilterOperator.Equal, "a");
    private static readonly FieldFilter Views = new("views", FilterOperator.GreaterThan, 1L);

    [Fact]
    public void FiltersAreEqualByValue()
    {
        new FieldFilter("title", FilterOperator.Equal, "a").ShouldBe(Title);
        new And(Title, Views).ShouldBe(new And(new FieldFilter("title", FilterOperator.Equal, "a"), Views));
        new Or(Title, Views).ShouldBe(new Or(Title, Views));
        new Not(Title).ShouldBe(new Not(new FieldFilter("title", FilterOperator.Equal, "a")));

        new FieldFilter("title", FilterOperator.Equal, "a").GetHashCode().ShouldBe(Title.GetHashCode());
        new And(Title, Views).GetHashCode().ShouldBe(new And(Title, Views).GetHashCode());
    }

    [Fact]
    public void TheHashCodeOfAGroupFollowsItsConditions()
    {
        var hashes = Enumerable.Range(0, 50)
            .Select(index => new And(new FieldFilter($"field{index}", FilterOperator.Equal, "a"), Views).GetHashCode())
            .Distinct()
            .Count();
        hashes.ShouldBeGreaterThan(1);

        new Or(Title, Views).GetHashCode().ShouldBe(new Or(new FieldFilter("title", FilterOperator.Equal, "a"), Views).GetHashCode());
        new Or(Title, Views).GetHashCode().ShouldNotBe(new Or(new FieldFilter("other", FilterOperator.Equal, "a"), Views).GetHashCode());
    }

    [Fact]
    public void FiltersDifferByPathOperatorOperandAndOrder()
    {
        new FieldFilter("other", FilterOperator.Equal, "a").ShouldNotBe(Title);
        new FieldFilter("title", FilterOperator.NotEqual, "a").ShouldNotBe(Title);
        new FieldFilter("title", FilterOperator.Equal, "b").ShouldNotBe(Title);
        new And(Title, Views).ShouldNotBe(new And(Views, Title));
        new Or(Title, Views).ShouldNotBe(new Or(Title));
        ((ContentFilter)new And(Title)).ShouldNotBe(new Or(Title));
    }

    [Fact]
    public void AListOperandIsComparedItemByItem()
    {
        var list = new FieldFilter("a", FilterOperator.In, new object?[] { "x", "y" });

        list.ShouldBe(new FieldFilter("a", FilterOperator.In, new List<object?> { "x", "y" }));
        list.ShouldNotBe(new FieldFilter("a", FilterOperator.In, new object?[] { "y", "x" }));
        new FieldFilter("a", FilterOperator.Equal, "xy").ShouldNotBe(new FieldFilter("a", FilterOperator.Equal, "xy".ToCharArray()));
    }

    [Fact]
    public void AnEmptyQueryAsksForTheDefaults()
    {
        var query = new ContentQuery();

        query.Filter.ShouldBeNull();
        query.Sort.ShouldBeEmpty();
        query.Page.ShouldBeNull();
        query.Select.ShouldBeEmpty();
        query.Populate.ShouldBeEmpty();
        query.Status.ShouldBeNull();
        query.Fallback.ShouldBe(LocaleFallback.Chain);
        new PageRequest().WithTotal.ShouldBeTrue();
    }
}
