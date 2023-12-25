using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core;

public abstract class BaseComponent : ComponentBase
{
    // Child content
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

	[Parameter]
	public string Class {get; set;}

    // Additional Attributes
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> AdditionalAttributes { get; set; } = default!;

    // Component Name
    public string ComponentName { get; }

    public BaseComponent()
    {
        ComponentName = GetType().Name.FromPascalCaseToKebabCase();
    }
}
