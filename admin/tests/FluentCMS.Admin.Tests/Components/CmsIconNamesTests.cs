using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// The icons an admin half names by text, such as the icon of a menu item.
public sealed class CmsIconNamesTests : ComponentTestContext
{
    public static TheoryData<CmsIconName> Icons() => new(Enum.GetValues<CmsIconName>());

    [Theory]
    [MemberData(nameof(Icons))]
    public void EveryIconHasItsNameInCamelCase(CmsIconName icon)
    {
        var name = char.ToLowerInvariant(icon.ToString()[0]) + icon.ToString()[1..];

        CmsIconNames.All.ShouldContain(name);
        CmsIconNames.Find(name).ShouldBe(icon);
        CmsIconNames.TryFind(name, out var found).ShouldBeTrue();
        found.ShouldBe(icon);
    }

    [Fact]
    public void TheNamesAreTheIconsOfTheSetAndNoMore() =>
        CmsIconNames.All.Count.ShouldBe(Enum.GetValues<CmsIconName>().Length);

    [Theory]
    [InlineData("comment")]
    [InlineData("Comment")]
    [InlineData("COMMENT")]
    public void ANameIsFoundWhateverItsCase(string name) =>
        CmsIconNames.Find(name).ShouldBe(CmsIconName.Comment);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rocket")]
    [InlineData(" comment")]
    [InlineData("24")]
    public void AnUnknownNameFallsBackToTheIconOfAPluginAndDoesNotThrow(string? name)
    {
        CmsIconNames.Find(name).ShouldBe(CmsIconName.Plugin);
        CmsIconNames.Default.ShouldBe(CmsIconName.Plugin);
        CmsIconNames.TryFind(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void AnIconOfAnUnknownNameIsDrawn()
    {
        var unknown = Render<CmsIcon>(p => p.Add(x => x.Name, CmsIconNames.Find("rocket")));
        var plugin = Render<CmsIcon>(p => p.Add(x => x.Name, CmsIconName.Plugin));

        unknown.Find("path").GetAttribute("d").ShouldBe(plugin.Find("path").GetAttribute("d"));
    }
}
