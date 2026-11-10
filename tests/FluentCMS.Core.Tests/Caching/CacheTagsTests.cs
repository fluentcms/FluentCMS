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
}
