using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// The library's checkbox and switch carry their role on the host element, so the ARIA attributes go there.
public sealed class CmsChoiceTests : ComponentTestContext
{
    [Fact]
    public void TheCheckboxIsLabelledAndCarriesHelpRequiredAndInvalidOnItsHost()
    {
        var checkbox = Render<CmsCheckbox>(p => p
            .Add(x => x.Label, "I accept")
            .Add(x => x.Required, true)
            .Add(x => x.Error, "Accept to go on."));

        var host = checkbox.Find("fluent-checkbox");
        checkbox.Markup.ShouldContain("I accept");
        host.GetAttribute("aria-required").ShouldBe("true");
        host.GetAttribute("aria-invalid").ShouldBe("true");
        checkbox.Find($"#{host.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Accept to go on.");
    }

    [Fact]
    public void TheSwitchIsLabelledAndDescribedOnItsHost()
    {
        var toggle = Render<CmsSwitch>(p => p.Add(x => x.Label, "Allow comments").Add(x => x.Help, "Readers can reply."));

        var host = toggle.Find("fluent-switch");
        toggle.Markup.ShouldContain("Allow comments");
        toggle.Find($"#{host.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Readers can reply.");
        host.HasAttribute("aria-invalid").ShouldBeFalse();
    }
}
