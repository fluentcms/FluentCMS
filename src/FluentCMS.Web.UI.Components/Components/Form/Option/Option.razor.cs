namespace FluentCMS.Web.UI.Components;

public partial class Option<TValue>
{
    [Parameter]
    public bool Selected { get; set; }

    [Parameter]
    public TValue? Value { get; set; }
}
