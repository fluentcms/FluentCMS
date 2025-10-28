namespace FluentCMS.Web.UI.Components;

public class BaseComponentWithContent : BaseComponent
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override RenderFragment BuildContent => ChildContent ?? (__builder => { });
}
