using Bunit;
using FluentCMS.Admin.Components.Layout;

namespace FluentCMS.Tests.Admin;

public sealed class ReconnectModalTests
{
    [Fact]
    public void Renders_the_element_Blazor_looks_for_with_a_part_for_each_reconnect_state()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ReconnectModal>();

        var modal = cut.Find("#components-reconnect-modal");
        var parts = cut.FindAll("[data-reconnect-when]");

        Assert.Equal("status", modal.GetAttribute("role"));
        Assert.Equal("polite", modal.GetAttribute("aria-live"));
        Assert.True(modal.HasAttribute("data-permanent"));
        Assert.Equal(["show", "retrying", "failed", "paused", "resume-failed"], parts.Select(part => part.GetAttribute("data-reconnect-when")));
        Assert.Equal("Connection lost. Rejoining the server…", parts[0].TextContent);
    }

    [Fact]
    public void The_countdown_sits_where_the_sentence_puts_it()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ReconnectModal>();

        var retrying = cut.Find("[data-reconnect-when=retrying]");

        Assert.Equal("components-seconds-to-next-attempt", retrying.QuerySelector("span")!.Id);
        Assert.Equal("Rejoin failed. Trying again in <span id=\"components-seconds-to-next-attempt\"></span> seconds.", retrying.InnerHtml);
    }

    [Fact]
    public void Each_failure_offers_its_button_for_admin_js()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ReconnectModal>();

        Assert.Equal("retry", cut.Find("[data-reconnect-when=failed] button").GetAttribute("data-reconnect"));
        Assert.Equal("resume", cut.Find("[data-reconnect-when=paused] button").GetAttribute("data-reconnect"));
        Assert.Equal("reload", cut.Find("[data-reconnect-when=resume-failed] button").GetAttribute("data-reconnect"));
        Assert.All(cut.FindAll("button"), button => Assert.Equal("button", button.GetAttribute("type")));
    }

    [Fact]
    public void Needs_no_inline_style_or_script_so_the_content_security_policy_holds()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ReconnectModal>();

        // Visibility per state comes from app.css, so the element and its parts carry no display utility that would override it.
        Assert.Empty(cut.FindAll("[style], script, style"));
        Assert.All(cut.FindAll("#components-reconnect-modal, [data-reconnect-when]"), element =>
            Assert.DoesNotContain(element.ClassList, name => name is "hidden" or "block" or "flex" or "grid" or "inline-flex" or "contents"));
    }
}
