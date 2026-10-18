using System.Text.RegularExpressions;

namespace FluentCMS.Tests.Admin;

// Reads the stylesheet the build compiles with Tailwind, the one the host serves, with whitespace and quotes removed.
public sealed class StylesheetTests
{
    private static readonly string Css = Regex.Replace(
        File.ReadAllText(Path.Combine(ProjectReferenceRulesTests.FindRepositoryRoot(), "src", "FluentCMS.Admin", "wwwroot", "css", "app.generated.css")),
        "[\\s\"']",
        string.Empty);

    [Fact]
    public void Dark_applies_with_the_dark_class_or_when_nothing_is_stored_and_the_system_prefers_it()
    {
        Assert.Contains(":root{color-scheme:light;", Css);
        Assert.Contains(":root:where(.dark,.dark*){color-scheme:dark;", Css);
        Assert.Contains("@media(prefers-color-scheme:dark){:root:where(:root:not(.light),:root:not(.light)*){color-scheme:dark;", Css);
    }

    [Fact]
    public void The_reconnect_display_is_hidden_until_Blazor_reports_a_state_and_then_shows_only_that_part()
    {
        Assert.Contains("#components-reconnect-modal{display:none}", Css);
        Assert.Contains("#components-reconnect-modal[data-reconnect-when]{display:none}", Css);
        Assert.Contains("#components-reconnect-modal.components-reconnect-show:not(.components-reconnect-retrying)[data-reconnect-when=show]", Css);
        Assert.Contains("#components-reconnect-modal.components-reconnect-failed[data-reconnect-when=failed]", Css);
        Assert.Contains("#components-reconnect-modal.components-reconnect-resume-failed[data-reconnect-when=resume-failed]", Css);
    }
}
