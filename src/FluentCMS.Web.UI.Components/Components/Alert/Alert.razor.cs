using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class Alert : BaseComponent 
{
    [Parameter]
    public bool Dismissible { get; set; }

    [Parameter]
    public bool Show { get; set; } = true;

    [Parameter]
    [CSSProperty]
    public AlertType Type { get; set; }

    public void Close()
    {
        Show = false;
    }
}