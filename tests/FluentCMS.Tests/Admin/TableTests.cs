using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class TableTests
{
    [Fact]
    public void Renders_a_native_table_with_the_shadcn_slots_and_column_headers()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Table>(table => table
            .AddUnmatched("aria-labelledby", "users-heading")
            .AddChildContent<TableHeader>(header => header
                .AddChildContent<TableRow>(row => row
                    .AddChildContent<TableHead>(head => head.AddChildContent("Name"))
                    .AddChildContent<TableHead>(head => head.Add(h => h.Class, "text-end").AddChildContent("Actions"))))
            .AddChildContent<TableBody>(body => body
                .AddChildContent<TableRow>(row => row
                    .AddUnmatched("data-testid", "user-row")
                    .AddChildContent<TableCell>(cell => cell.AddChildContent("Owner"))
                    .AddChildContent<TableCell>(cell => cell.AddUnmatched("data-label", "actions").AddChildContent("Disable")))));

        var table = cut.Find("table");
        var heads = cut.FindAll("th");
        var cells = cut.FindAll("td");

        Assert.Equal("table-container", table.ParentElement!.GetAttribute("data-slot"));
        Assert.Contains("overflow-x-auto", table.ParentElement.ClassList);
        Assert.Equal("users-heading", table.GetAttribute("aria-labelledby"));
        Assert.NotNull(cut.Find("thead[data-slot=table-header] > tr > th"));
        Assert.NotNull(cut.Find("tbody[data-slot=table-body] > tr[data-testid=user-row] > td"));
        Assert.All(heads, head => Assert.Equal("col", head.GetAttribute("scope")));
        Assert.Contains("text-start", heads[0].ClassList);
        Assert.Contains("text-end", heads[1].ClassList);
        Assert.Equal(["Owner", "Disable"], cells.Select(cell => cell.TextContent.Trim()));
        Assert.Equal("actions", cells[1].GetAttribute("data-label"));
    }

    [Fact]
    public void Uses_logical_utilities_only_so_the_table_follows_the_root_direction()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Table>(table => table
            .AddChildContent<TableHeader>(header => header
                .AddChildContent<TableRow>(row => row.AddChildContent<TableHead>(head => head.AddChildContent("Name"))))
            .AddChildContent<TableBody>(body => body
                .AddChildContent<TableRow>(row => row.AddChildContent<TableCell>(cell => cell.AddChildContent("Owner")))));

        var classes = cut.FindAll("[class]").SelectMany(element => element.ClassList).ToList();

        Assert.DoesNotContain(classes, value => value.Contains("text-left") || value.Contains("text-right") || value.Contains("pr-") || value.Contains("pl-"));
    }
}
