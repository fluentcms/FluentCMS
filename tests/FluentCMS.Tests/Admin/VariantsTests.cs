using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class VariantsTests
{
    private static readonly Variants Alert = new(
        "relative w-full rounded-lg border px-4 py-3 text-sm",
        new()
        {
            ["variant"] = new()
            {
                ["default"] = "bg-card text-card-foreground",
                ["destructive"] = "bg-card text-destructive",
            },
            ["size"] = new()
            {
                ["sm"] = "px-3 py-2 text-xs",
            },
        },
        new() { ["variant"] = "default" });

    [Fact]
    public void Without_a_choice_the_defaults_apply_and_a_variant_without_a_default_adds_nothing()
    {
        Assert.Equal("relative w-full rounded-lg border px-4 py-3 text-sm bg-card text-card-foreground", Alert.Classes());
    }

    [Fact]
    public void A_chosen_value_replaces_the_base_classes_it_conflicts_with()
    {
        Assert.Equal(
            "relative w-full rounded-lg border bg-card text-destructive px-3 py-2 text-xs",
            Alert.Classes(new() { ["variant"] = "destructive", ["size"] = "sm" }));
    }

    [Fact]
    public void A_null_choice_falls_back_to_the_default_and_an_unknown_value_adds_nothing()
    {
        Assert.Equal(Alert.Classes(), Alert.Classes(new() { ["variant"] = null }));
        Assert.Equal("relative w-full rounded-lg border px-4 py-3 text-sm", Alert.Classes(new() { ["variant"] = "unknown" }));
    }

    [Fact]
    public void The_class_parameter_comes_last_and_wins()
    {
        Assert.Equal(
            "relative w-full border px-4 py-3 text-sm bg-card text-card-foreground rounded-none mt-2",
            Alert.Classes(className: "rounded-none mt-2"));
    }
}
