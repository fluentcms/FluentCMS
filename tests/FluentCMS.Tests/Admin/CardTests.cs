using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class CardTests
{
    [Fact]
    public void Renders_the_shadcn_slots_in_order_with_the_page_heading_inside_the_title()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Card>(card => card
            .AddUnmatched("data-testid", "sign-in-card")
            .AddChildContent<CardHeader>(header => header
                .AddChildContent<CardTitle>(title => title.AddChildContent("<h1>Sign in</h1>"))
                .AddChildContent<CardDescription>(description => description.AddChildContent("Enter your email.")))
            .AddChildContent<CardContent>(content => content.Add(c => c.Class, "grid gap-6").AddChildContent("<form></form>")));

        var card = cut.Find("[data-slot=card]");
        var slots = card.QuerySelectorAll("[data-slot]").Select(element => element.GetAttribute("data-slot"));

        Assert.Equal("sign-in-card", card.GetAttribute("data-testid"));
        Assert.Contains("rounded-xl", card.ClassList);
        Assert.Contains("bg-card", card.ClassList);
        Assert.Equal(["card-header", "card-title", "card-description", "card-content"], slots);
        Assert.Equal("Sign in", cut.Find("[data-slot=card-title] > h1").TextContent);
        Assert.Contains("text-muted-foreground", cut.Find("[data-slot=card-description]").ClassList);
        Assert.Contains("grid", cut.Find("[data-slot=card-content]").ClassList);
        Assert.Contains("px-6", cut.Find("[data-slot=card-content]").ClassList);
    }
}
