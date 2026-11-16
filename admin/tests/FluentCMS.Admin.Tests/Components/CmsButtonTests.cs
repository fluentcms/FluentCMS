using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsButtonTests : ComponentTestContext
{
    [Fact]
    public void AButtonWithTextIsNamedByItsText()
    {
        var button = Render<CmsButton>(p => p.AddChildContent("Save").Add(x => x.Icon, CmsIconName.Save));

        var host = button.Find("fluent-button");
        host.TextContent.Trim().ShouldBe("Save");
        host.HasAttribute("aria-label").ShouldBeFalse();
        host.GetAttribute("type").ShouldBe("button");
    }

    [Fact]
    public void AnIconOnlyButtonIsNamedByItsLabel()
    {
        var button = Render<CmsButton>(p => p.Add(x => x.Icon, CmsIconName.Delete).Add(x => x.Label, "Delete"));

        var host = button.Find("fluent-button");
        host.GetAttribute("aria-label").ShouldBe("Delete");
        host.GetAttribute("title").ShouldBe("Delete");
        host.QuerySelector("svg")!.GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void AnIconOnlyButtonWithoutALabelIsRefused()
    {
        Should.Throw<InvalidOperationException>(() => Render<CmsButton>(p => p.Add(x => x.Icon, CmsIconName.Delete)));
    }

    [Fact]
    public void ASubmitButtonSubmits()
    {
        var button = Render<CmsButton>(p => p.AddChildContent("Save").Add(x => x.Submit, true));

        button.Find("fluent-button").GetAttribute("type").ShouldBe("submit");
    }

    [Fact]
    public void ClickingRaisesOnClick()
    {
        var clicks = 0;
        var button = Render<CmsButton>(p => p.AddChildContent("Save").Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));

        button.Find("fluent-button").Click();

        clicks.ShouldBe(1);
    }

    [Fact]
    public void ADecorativeIconIsHiddenFromAssistiveTechnology()
    {
        var icon = Render<CmsIcon>(p => p.Add(x => x.Name, CmsIconName.Info));

        icon.Find("svg").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void ALabelledIconIsAnImageWithThatName()
    {
        var icon = Render<CmsIcon>(p => p.Add(x => x.Name, CmsIconName.Warning).Add(x => x.Label, "Warning"));

        var image = icon.Find("[role=img]");
        image.GetAttribute("aria-label").ShouldBe("Warning");
        image.QuerySelector("svg")!.GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Theory]
    [MemberData(nameof(IconNames))]
    public void EveryIconNameHasAnIcon(CmsIconName name)
    {
        var icon = Render<CmsIcon>(p => p.Add(x => x.Name, name));

        icon.Find("svg").InnerHtml.ShouldContain("path");
    }

    public static TheoryData<CmsIconName> IconNames() => new(Enum.GetValues<CmsIconName>());
}
