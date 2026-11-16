using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// Selecting a row must not re-create the grid's cells: that drops keyboard focus to the document (review of #3172).
public sealed class CmsDataGridNestingTests : ComponentTestContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingARowKeepsEveryCell(bool nested)
    {
        var host = Render<GridHost>(p => p.Add(x => x.Nested, nested));
        var before = host.FindComponents<FluentDataGridCell<Item>>().Select(c => c.Instance).ToList();
        before.ShouldNotBeEmpty();

        host.FindAll("tbody td[col-select]")[0].Click();

        host.Instance.Selected.Count.ShouldBe(1);
        var after = host.FindComponents<FluentDataGridCell<Item>>().Select(c => c.Instance).ToList();
        after.Count(before.Contains).ShouldBe(before.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingARowByKeyboardKeepsEveryCell(bool nested)
    {
        var host = Render<GridHost>(p => p.Add(x => x.Nested, nested));
        var before = host.FindComponents<FluentDataGridCell<Item>>().Select(c => c.Instance).ToList();

        // The library selects from the row's keydown while it walks its columns; a render inside that walk throws.
        host.FindAll("tbody tr")[0].KeyDown(new KeyboardEventArgs { Code = "Enter", Key = "Enter" });

        host.Instance.Selected.Count.ShouldBe(1);
        host.FindComponents<FluentDataGridCell<Item>>().Select(c => c.Instance).Count(before.Contains).ShouldBe(before.Count);
    }

    public sealed record Item(string Name);

    private sealed class GridHost : ComponentBase
    {
        private static readonly IReadOnlyList<Item> Items = [new("Alpha"), new("Bravo")];

        [Parameter]
        public bool Nested { get; set; }

        public IReadOnlyList<Item> Selected { get; private set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Nested)
            {
                builder.OpenComponent<CmsStack>(0);
                builder.AddAttribute(1, nameof(CmsStack.ChildContent), (RenderFragment)(stack =>
                {
                    stack.OpenComponent<CmsCard>(0);
                    stack.AddAttribute(1, nameof(CmsCard.Title), "Items");
                    stack.AddAttribute(2, nameof(CmsCard.ChildContent), (RenderFragment)RenderGrid);
                    stack.CloseComponent();
                }));
                builder.CloseComponent();
            }
            else
            {
                RenderGrid(builder);
            }
        }

        private void RenderGrid(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CmsDataGrid<Item>>(10);
            builder.AddAttribute(11, nameof(CmsDataGrid<Item>.Label), "Items");
            builder.AddAttribute(12, nameof(CmsDataGrid<Item>.Items), Items);
            builder.AddAttribute(13, nameof(CmsDataGrid<Item>.SelectionMode), CmsSelectionMode.Many);
            builder.AddAttribute(14, nameof(CmsDataGrid<Item>.SelectedItems), Selected);
            builder.AddAttribute(15, nameof(CmsDataGrid<Item>.SelectedItemsChanged), EventCallback.Factory.Create<IReadOnlyList<Item>>(this, items => Selected = items));
            builder.AddAttribute(16, nameof(CmsDataGrid<Item>.ChildContent), (RenderFragment)(columns =>
            {
                columns.OpenComponent<CmsDataGridColumn<Item>>(0);
                columns.AddAttribute(1, nameof(CmsDataGridColumn<Item>.Title), "Name");
                columns.AddAttribute(2, nameof(CmsDataGridColumn<Item>.Value), (Func<Item, string?>)(i => i.Name));
                columns.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
