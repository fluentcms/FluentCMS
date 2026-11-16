using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsDataGridTests : ComponentTestContext
{
    private static readonly IReadOnlyList<Article> Articles =
    [
        new("Bravo", 2),
        new("Alpha", 3),
        new("Charlie", 1),
    ];

    [Fact]
    public void TheGridIsNamedAndShowsItsColumnsAndRows()
    {
        var grid = RenderGrid(CmsSelectionMode.None);

        var table = grid.Find("table");
        table.GetAttribute("role").ShouldBe("grid");
        table.GetAttribute("aria-label").ShouldBe("Articles");
        grid.FindAll("th").Select(th => th.TextContent.Trim()).ShouldBe(["Title", "Rank"]);
        FirstColumn(grid).ShouldBe(["Bravo", "Alpha", "Charlie"]);
    }

    [Fact]
    public void SortingByAColumnSortsTheRowsAndAnnouncesIt()
    {
        var grid = RenderGrid(CmsSelectionMode.None);

        SortBy(grid, "Title");

        grid.WaitForAssertion(() => FirstColumn(grid).ShouldBe(["Alpha", "Bravo", "Charlie"]));
        Status(grid).ShouldBe("Sorted by Title, ascending");
        grid.Find("th[aria-sort=ascending]").TextContent.ShouldContain("Title");

        SortBy(grid, "Title");

        grid.WaitForAssertion(() => FirstColumn(grid).ShouldBe(["Charlie", "Bravo", "Alpha"]));
        Status(grid).ShouldBe("Sorted by Title, descending");
    }

    [Fact]
    public void AColumnWithoutASortKeyCannotBeSorted()
    {
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add<CmsDataGridColumn<Article>>(x => x.ChildContent, c => c.Add(x => x.Title, "Title").Add(x => x.Value, a => a.Title)));

        grid.FindAll("th fluent-button").ShouldBeEmpty();
    }

    [Fact]
    public void SelectingARowRaisesTheSelectionAndAnnouncesTheCount()
    {
        IReadOnlyList<Article> selected = [];
        var grid = RenderGrid(CmsSelectionMode.Many, items => selected = items);

        grid.FindAll("tbody td[col-select]")[1].Click();

        selected.Select(a => a.Title).ShouldBe(["Alpha"]);
        Status(grid).ShouldBe("Rows selected: 1");
    }

    [Fact]
    public void ACellTemplateRendersTheCell()
    {
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add<CmsDataGridColumn<Article>>(x => x.ChildContent, c => c
                .Add(x => x.Title, "Title")
                .Add(x => x.ChildContent, (RenderFragment<Article>)(a => b => b.AddMarkupContent(0, $"<strong>{a.Title}</strong>")))));

        grid.WaitForAssertion(() => grid.FindAll("tbody strong").Count.ShouldBe(3));
    }

    [Fact]
    public void AColumnOutsideAGridIsRefused()
    {
        Should.Throw<InvalidOperationException>(() => Render<CmsDataGridColumn<Article>>(p => p.Add(x => x.Title, "Title")));
    }

    private IRenderedComponent<CmsDataGrid<Article>> RenderGrid(CmsSelectionMode mode, Action<IReadOnlyList<Article>>? selected = null) =>
        Render<CmsDataGrid<Article>>(p =>
        {
            p.Add(x => x.Label, "Articles")
             .Add(x => x.Items, Articles)
             .Add(x => x.SelectionMode, mode)
             .Add(x => x.ChildContent, (RenderFragment)(b =>
             {
                 b.OpenComponent<CmsDataGridColumn<Article>>(0);
                 b.AddAttribute(1, nameof(CmsDataGridColumn<Article>.Title), "Title");
                 b.AddAttribute(2, nameof(CmsDataGridColumn<Article>.Value), (Func<Article, string?>)(a => a.Title));
                 b.AddAttribute(3, nameof(CmsDataGridColumn<Article>.SortKey), (Func<Article, IComparable?>)(a => a.Title));
                 b.CloseComponent();
                 b.OpenComponent<CmsDataGridColumn<Article>>(4);
                 b.AddAttribute(5, nameof(CmsDataGridColumn<Article>.Title), "Rank");
                 b.AddAttribute(6, nameof(CmsDataGridColumn<Article>.Value), (Func<Article, string?>)(a => a.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                 b.CloseComponent();
             }));
            if (selected is not null)
            {
                p.Add(x => x.SelectedItemsChanged, selected);
            }
        });

    private static void SortBy(IRenderedComponent<CmsDataGrid<Article>> grid, string title) =>
        grid.FindAll("th fluent-button").Single(b => b.TextContent.Contains(title, StringComparison.Ordinal)).Click();

    private static List<string> FirstColumn(IRenderedComponent<CmsDataGrid<Article>> grid) =>
        grid.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td:not([col-select])")[0].TextContent.Trim()).ToList();

    private static string Status(IRenderedComponent<CmsDataGrid<Article>> grid) => grid.Find("[role=status]").TextContent.Trim();

    public sealed record Article(string Title, int Rank);
}
