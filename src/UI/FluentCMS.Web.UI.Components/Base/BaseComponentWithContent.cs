namespace FluentCMS.Web.UI.Components;

public abstract class BaseComponentWithContent : BaseComponent
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override RenderFragment BuildContent => ChildContent ?? (__builder => { });
}
