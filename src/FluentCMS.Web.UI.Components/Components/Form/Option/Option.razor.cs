namespace FluentCMS.Web.UI.Components;

public partial class Option
{
    [Parameter]
    public bool Selected { get; set; }

    [Parameter]
    public TValue? Value { get; set; }
}
