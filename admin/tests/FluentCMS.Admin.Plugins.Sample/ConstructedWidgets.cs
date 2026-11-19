using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FluentCMS.Admin.Plugins.Sample;

// Widgets the home page's tests name in a manifest of their own; none is declared. The renderer gives a constructor the
// admin's services, so the first renders, the second takes a service only the half's provider gives, and the third has an
// [Inject] property without a setter.
public sealed class ConstructedWidget(IAdminPluginServices plugins) : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "h2");
        builder.AddContent(1, plugins is null ? null : "Constructed");
        builder.CloseElement();
    }
}

public sealed class MisconstructedWidget(ReviewsApi api) : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddContent(0, api.Approved);
    }
}

public sealed class SetterlessWidget : ComponentBase
{
    [Inject]
    public NavigationManager Navigation { get; } = null!;
}
