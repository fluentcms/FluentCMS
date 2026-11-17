using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsDataGridTests : ComponentTestContext
{
    private static readonly IReadOnlyList<Article> Articles =
    [
        new("Bravo", 2),
        new("Alpha", 2),
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

        FirstColumn(grid).ShouldBe(["Alpha", "Bravo", "Charlie"]);
        Status(grid).ShouldBe("Sorted by Title, ascending");
        grid.Find("th[aria-sort=ascending]").TextContent.ShouldContain("Title");
        grid.Find("th[aria-sort=none]").TextContent.ShouldContain("Rank");

        SortBy(grid, "Title");

        FirstColumn(grid).ShouldBe(["Charlie", "Bravo", "Alpha"]);
        Status(grid).ShouldBe("Sorted by Title, descending");
    }

    [Fact]
    public void ShiftWithAClickAddsAKeyAndTheLaterKeysAreSaidInTheButton()
    {
        var grid = RenderGrid(CmsSelectionMode.None);

        SortBy(grid, "Rank");
        SortBy(grid, "Title", adding: true);

        FirstColumn(grid).ShouldBe(["Charlie", "Alpha", "Bravo"]);
        Status(grid).ShouldBe("Sorted by Rank, ascending, then by Title, ascending");
        grid.FindAll("th[aria-sort=ascending]").ShouldHaveSingleItem().TextContent.ShouldContain("Rank");
        Header(grid, "Title").QuerySelector(".cms-visually-hidden")!.TextContent.ShouldBe("sort key 2 of 2");

        // A click without Shift sorts by the column alone again.
        SortBy(grid, "Title");

        FirstColumn(grid).ShouldBe(["Alpha", "Bravo", "Charlie"]);
        Status(grid).ShouldBe("Sorted by Title, ascending");
    }

    [Fact]
    public void AGridWhoseCallerSortsRaisesTheSortAndLeavesTheRowsAsGiven()
    {
        IReadOnlyList<CmsSortKey>? asked = null;
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add(x => x.Sort, [new CmsSortKey("rank", Descending: true)])
            .Add(x => x.SortChanged, keys => asked = keys)
            .Add(x => x.ChildContent, Columns(sortable: true)));

        grid.Find("th[aria-sort=descending]").TextContent.ShouldContain("Rank");

        SortBy(grid, "Title", adding: true);

        asked.ShouldBe([new CmsSortKey("rank", Descending: true), new CmsSortKey("title")]);
        FirstColumn(grid).ShouldBe(["Bravo", "Alpha", "Charlie"]);
        Status(grid).ShouldBe("Sorted by Rank, descending, then by Title, ascending");
    }

    [Fact]
    public void AColumnWithoutASortKeyCannotBeSorted()
    {
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add<CmsDataGridColumn<Article>>(x => x.ChildContent, c => c.Add(x => x.Title, "Title").Add(x => x.Value, a => a.Title)));

        grid.FindAll("th button").ShouldBeEmpty();
        grid.Find("th").HasAttribute("aria-sort").ShouldBeFalse();
    }

    [Fact]
    public void ARowIsSelectedByItsNamedCheckboxAndTheCountIsAnnounced()
    {
        IReadOnlyList<Article> selected = [];
        var grid = RenderGrid(CmsSelectionMode.Many, items => selected = items);

        var checkbox = Row(grid, 1).QuerySelector("input[type=checkbox]")!;
        checkbox.GetAttribute("aria-label").ShouldBe("Select Alpha");
        checkbox.Change(true);

        selected.Select(a => a.Title).ShouldBe(["Alpha"]);
        Status(grid).ShouldBe("Rows selected: 1");
        Row(grid, 1).GetAttribute("aria-selected").ShouldBe("true");
        Row(grid, 0).GetAttribute("aria-selected").ShouldBe("false");
        grid.Find("table").GetAttribute("aria-multiselectable").ShouldBe("true");
    }

    [Fact]
    public void TheHeaderCheckboxSelectsAndClearsEveryRowOfThePageAndIsMixedInBetween()
    {
        IReadOnlyList<Article> selected = [];
        var grid = RenderGrid(CmsSelectionMode.Many, items => selected = items);
        AllBox(grid).GetAttribute("aria-label").ShouldBe("Select all rows on this page");
        AllBox(grid).GetAttribute("aria-checked").ShouldBe("false");

        AllBox(grid).Click();

        selected.Count.ShouldBe(3);
        grid.FindAll("tbody tr[aria-selected=true]").Count.ShouldBe(3);
        AllBox(grid).GetAttribute("aria-checked").ShouldBe("true");

        Row(grid, 0).QuerySelector("input[type=checkbox]")!.Change(false);

        AllBox(grid).GetAttribute("aria-checked").ShouldBe("mixed");

        // From the mixed state the control selects every row, then clears them.
        AllBox(grid).Click();
        selected.Count.ShouldBe(3);
        AllBox(grid).Click();
        selected.ShouldBeEmpty();
    }

    [Fact]
    public void TheArrowKeysMoveFromARowToTheNext()
    {
        var grid = RenderGrid(CmsSelectionMode.Many);

        // Down from the first row and up from the last reach the same row; up from it reaches another.
        Row(grid, 0).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        var second = Focused();
        Row(grid, 2).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Focused().ShouldBe(second);
        Row(grid, 1).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Focused().ShouldNotBe(second);

        // Past the first or last row nothing moves.
        var focuses = Focuses();
        Row(grid, 0).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Row(grid, 2).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Focuses().ShouldBe(focuses);
    }

    [Fact]
    public void ShiftWithEnterAddsASortKeyAndTheButtonsSaySo()
    {
        var grid = RenderGrid(CmsSelectionMode.None);
        SortBy(grid, "Rank");
        var title = Header(grid, "Title").QuerySelector("button")!;

        title.KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        title = Header(grid, "Title").QuerySelector("button")!;
        title.Click();
        title = Header(grid, "Title").QuerySelector("button")!;

        Status(grid).ShouldBe("Sorted by Rank, ascending, then by Title, ascending");
        var hint = grid.Find($"#{title.GetAttribute("aria-describedby")}");
        hint.TextContent.ShouldBe("Shift with Enter or a click adds the column to the sort.");
        grid.FindAll("th button").ShouldAllBe(button => button.GetAttribute("aria-describedby") == hint.Id);
    }

    // One tab stop per row: the checkbox when rows can be selected, the row otherwise; no cell is one (#3178).
    [Fact]
    public void EachRowIsOneTabStop()
    {
        var selecting = RenderGrid(CmsSelectionMode.Many);
        selecting.FindAll("tbody tr").ShouldAllBe(row => !row.HasAttribute("tabindex"));
        selecting.FindAll("tbody td").ShouldAllBe(cell => !cell.HasAttribute("tabindex"));
        selecting.FindAll("tbody input[type=checkbox]").Count.ShouldBe(3);
        selecting.FindAll("tbody input[type=checkbox]").ShouldAllBe(box => !box.HasAttribute("tabindex"));

        var plain = RenderGrid(CmsSelectionMode.None);
        plain.FindAll("tbody tr").ShouldAllBe(row => row.GetAttribute("tabindex") == "0" && !row.HasAttribute("aria-selected"));
        plain.FindAll("tbody td").ShouldAllBe(cell => !cell.HasAttribute("tabindex"));
    }

    [Fact]
    public void EnterOnARowActivatesIt()
    {
        Article? opened = null;
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add(x => x.SelectionMode, CmsSelectionMode.Many)
            .Add(x => x.OnRowActivated, article => opened = article)
            .Add(x => x.ChildContent, Columns(sortable: false)));

        Row(grid, 2).KeyDown(new KeyboardEventArgs { Key = "Enter", Code = "Enter" });

        opened!.Title.ShouldBe("Charlie");
    }

    [Fact]
    public void ALaterColumnIsPlacedByItsOrder()
    {
        var host = Render<OrderedColumns>();
        Headers(host).ShouldBe(["Title", "Rank"]);

        host.Render(p => p.Add(x => x.ShowFirst, true));

        Headers(host).ShouldBe(["First", "Title", "Rank"]);

        host.Render(p => p.Add(x => x.ShowFirst, true).Add(x => x.Swap, true));

        Headers(host).ShouldBe(["First", "Rank", "Title"]);
    }

    [Fact]
    public void WhileLoadingTheTableIsBusyAndWithNoRowsItSaysSo()
    {
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, [])
            .Add(x => x.Loading, true)
            .Add(x => x.ChildContent, Columns(sortable: false)));

        grid.Find("table").GetAttribute("aria-busy").ShouldBe("true");
        grid.Find(".cms-data-grid-message td").TextContent.Trim().ShouldBe("Loading…");
        grid.Find(".cms-data-grid-message td").GetAttribute("colspan").ShouldBe("2");

        grid.Render(p => p.Add(x => x.Loading, false).Add(x => x.EmptyContent, (RenderFragment)(b => b.AddContent(0, "No articles."))));

        grid.Find("table").HasAttribute("aria-busy").ShouldBeFalse();
        grid.Find(".cms-data-grid-message td").TextContent.Trim().ShouldBe("No articles.");
    }

    [Fact]
    public void APageOfManyRowsSaysWhereItsRowsAre()
    {
        var grid = Render<CmsDataGrid<Article>>(p => p
            .Add(x => x.Label, "Articles")
            .Add(x => x.Items, Articles)
            .Add(x => x.TotalItems, 53L)
            .Add(x => x.RowOffset, 25L)
            .Add(x => x.ChildContent, Columns(sortable: false)));

        grid.Find("table").GetAttribute("aria-rowcount").ShouldBe("54");
        grid.FindAll("tbody tr").Select(row => row.GetAttribute("aria-rowindex")).ShouldBe(["27", "28", "29"]);
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

        grid.FindAll("tbody strong").Count.ShouldBe(3);
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
                 b.AddAttribute(7, nameof(CmsDataGridColumn<Article>.SortKey), (Func<Article, IComparable?>)(a => a.Rank));
                 b.CloseComponent();
             }));
            if (selected is not null)
            {
                p.Add(x => x.SelectedItemsChanged, selected);
            }
        });

    private static RenderFragment Columns(bool sortable) => b =>
    {
        b.OpenComponent<CmsDataGridColumn<Article>>(0);
        b.AddAttribute(1, nameof(CmsDataGridColumn<Article>.Title), "Title");
        b.AddAttribute(2, nameof(CmsDataGridColumn<Article>.Name), "title");
        b.AddAttribute(3, nameof(CmsDataGridColumn<Article>.Value), (Func<Article, string?>)(a => a.Title));
        b.AddAttribute(4, nameof(CmsDataGridColumn<Article>.Sortable), sortable);
        b.CloseComponent();
        b.OpenComponent<CmsDataGridColumn<Article>>(5);
        b.AddAttribute(6, nameof(CmsDataGridColumn<Article>.Title), "Rank");
        b.AddAttribute(7, nameof(CmsDataGridColumn<Article>.Name), "rank");
        b.AddAttribute(8, nameof(CmsDataGridColumn<Article>.Value), (Func<Article, string?>)(a => a.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        b.AddAttribute(9, nameof(CmsDataGridColumn<Article>.Sortable), sortable);
        b.CloseComponent();
    };

    private static void SortBy(IRenderedComponent<CmsDataGrid<Article>> grid, string title, bool adding = false) =>
        Header(grid, title).QuerySelector("button")!.Click(new MouseEventArgs { ShiftKey = adding });

    private static IElement Header(IRenderedComponent<CmsDataGrid<Article>> grid, string title) =>
        grid.FindAll("th").Single(th => th.QuerySelector("button > span")?.TextContent == title || th.TextContent.Trim() == title);

    private static IElement Row(IRenderedComponent<CmsDataGrid<Article>> grid, int index) => grid.FindAll("tbody tr")[index];

    private static IElement AllBox(IRenderedComponent<CmsDataGrid<Article>> grid) => grid.Find("thead button[role=checkbox]");

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");

    private string? Focused() => ((ElementReference)JSInterop.Invocations.Last(call => call.Identifier == "Blazor._internal.domWrapper.focus").Arguments[0]!).Id;

    private static List<string> FirstColumn(IRenderedComponent<CmsDataGrid<Article>> grid) =>
        grid.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td:not(.cms-data-grid-select)")[0].TextContent.Trim()).ToList();

    private static List<string> Headers(IRenderedComponent<OrderedColumns> host) =>
        host.FindAll("th").Select(th => th.TextContent.Trim()).ToList();

    private static string Status(IRenderedComponent<CmsDataGrid<Article>> grid) => grid.Find("[role=status]").TextContent.Trim();

    public sealed record Article(string Title, int Rank);

    // A column that appears after the others, as one the user turns on, is placed by its order and not appended.
    private sealed class OrderedColumns : ComponentBase
    {
        [Parameter]
        public bool ShowFirst { get; set; }

        [Parameter]
        public bool Swap { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CmsDataGrid<Article>>(0);
            builder.AddAttribute(1, nameof(CmsDataGrid<Article>.Label), "Articles");
            builder.AddAttribute(2, nameof(CmsDataGrid<Article>.Items), Articles);
            builder.AddAttribute(3, nameof(CmsDataGrid<Article>.ChildContent), (RenderFragment)(columns =>
            {
                Column(columns, 0, "Title", Swap ? 2 : 1);
                Column(columns, 10, "Rank", Swap ? 1 : 2);
                if (ShowFirst)
                {
                    Column(columns, 20, "First", 0);
                }
            }));
            builder.CloseComponent();
        }

        private static void Column(RenderTreeBuilder builder, int sequence, string title, int order)
        {
            builder.OpenComponent<CmsDataGridColumn<Article>>(sequence);
            builder.SetKey(title);
            builder.AddAttribute(sequence + 1, nameof(CmsDataGridColumn<Article>.Title), title);
            builder.AddAttribute(sequence + 2, nameof(CmsDataGridColumn<Article>.Order), order);
            builder.AddAttribute(sequence + 3, nameof(CmsDataGridColumn<Article>.Value), (Func<Article, string?>)(a => a.Title));
            builder.CloseComponent();
        }
    }
}
