using FluentCMS.Core.Caching;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Caching;

public sealed class CacheTagsTests
{
    private static readonly string[] Tags = ["site:a", "type:a:note", "entry:b"];

    [Theory]
    [InlineData("Cache-Tag", "site:a,type:a:note,entry:b")]
    [InlineData("Edge-Cache-Tag", "site:a,type:a:note,entry:b")]
    [InlineData("Surrogate-Key", "site:a type:a:note entry:b")]
    [InlineData("surrogate-key", "site:a type:a:note entry:b")]
    public void TheHeaderListsTheTagsAsItsCdnReadsThem(string header, string expected) =>
        CacheTags.HeaderValue(header, Tags).ShouldBe(expected);

    // Two tags and the separator between them, of exactly the budget.
    [Theory]
    [InlineData("Cache-Tag")]
    [InlineData("Surrogate-Key")]
    public void TheTagsAreListedOnlyWithinTheBudget(string header)
    {
        string[] tags = ["site:a", new('x', CacheTags.HeaderBudget - "site:a".Length - 1)];

        CacheTags.HeaderValueWithinBudget(header, tags).ShouldNotBeNull().Length.ShouldBe(CacheTags.HeaderBudget);
        CacheTags.HeaderValueWithinBudget(header, [.. tags[..1], tags[1] + "x"]).ShouldBeNull();
    }
}
