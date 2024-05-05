using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FluentCMS.Web.UI.Components;

public partial class CloseButton
{
    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }
}