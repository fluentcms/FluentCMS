namespace FluentCMS.Web.UI.Components;

public partial class Stack
{
    [Parameter]
    [CSSProperty]
    public StackItems? Items { get; set; }

    [Parameter]
    [CSSProperty]
    public StackJustify? Justify { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Vertical { get; set; }
}