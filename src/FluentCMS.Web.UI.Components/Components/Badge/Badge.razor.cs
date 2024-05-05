using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class Badge : BaseComponent
{
    [Parameter]
    [CSSProperty]
    public Color Color { get; set; } = Color.Default;
}
