namespace FluentCMS.Web.UI.Components;

public partial class Badge : BaseComponentWithContent
{
    [Parameter]
    [CssProperty]
    public Color Color { get; set; } = Color.Default;
}
