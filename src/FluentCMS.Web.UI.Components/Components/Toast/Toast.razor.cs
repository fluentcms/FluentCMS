namespace FluentCMS.Web.UI.Components

public partial class Toast
{
    [Parameter]
    public bool Dismissible { get; set; } = false;

    [Parameter]
    public bool Show { get; set; } = true;

    [Parameter]
    [CSSProperty]
    public ToastType Type { get; set; }

    public void Close()
    {
        Show = false;
    }
}
