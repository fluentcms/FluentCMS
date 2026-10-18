using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class CommandTests
{
    [Fact]
    public void Input_is_a_combobox_over_the_listbox_of_options()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Command>(command => command
            .AddChildContent<CommandInput>(input => input.Add(i => i.Placeholder, "Search"))
            .AddChildContent<CommandList>(list => list
                .Add(l => l.Label, "Commands")
                .AddChildContent<CommandEmpty>(empty => empty.AddChildContent("No results"))
                .AddChildContent<CommandGroup>(group => group
                    .Add(g => g.Heading, "Suggestions")
                    .AddChildContent<CommandItem>(item => item.Add(i => i.Value, "calendar").AddChildContent("Calendar"))
                    .AddChildContent<CommandItem>(item => item.Add(i => i.Value, "calculator").Add(i => i.Disabled, true).AddChildContent("Calculator")))));

        var input = cut.Find("input");
        var list = cut.Find("[role=listbox]");
        var options = cut.FindAll("[role=option]");

        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal(list.Id, input.GetAttribute("aria-controls"));
        Assert.Equal("list", input.GetAttribute("aria-autocomplete"));
        Assert.Equal("Commands", list.GetAttribute("aria-label"));
        Assert.True(cut.Find("[data-ui=command-empty]").HasAttribute("hidden"));
        Assert.Equal(cut.Find("[data-slot=command-group-heading]").Id, cut.Find("[role=group]").GetAttribute("aria-labelledby"));
        Assert.Equal("calendar", options[0].GetAttribute("data-value"));
        Assert.Equal("true", options[1].GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task Choosing_an_option_invokes_its_callback_unless_it_is_disabled()
    {
        using var context = new AdminTestContext();
        var chosen = new List<string>();

        var cut = context.Render<Command>(command => command
            .AddChildContent<CommandList>(list => list
                .AddChildContent<CommandItem>(item => item.Add(i => i.Value, "calendar").Add(i => i.OnSelect, () => chosen.Add("calendar")).AddChildContent("Calendar"))
                .AddChildContent<CommandItem>(item => item.Add(i => i.Value, "calculator").Add(i => i.Disabled, true).Add(i => i.OnSelect, () => chosen.Add("calculator")).AddChildContent("Calculator"))));

        var options = cut.FindAll("[role=option]");
        await options[0].ClickAsync(new());
        await options[1].ClickAsync(new());

        Assert.Equal(["calendar"], chosen);
    }
}
