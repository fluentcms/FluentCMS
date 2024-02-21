using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public abstract class BaseComponent : ComponentBase
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public string Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> AdditionalAttributes { get; set; } = default!;

    public string ComponentName { get; }

    public BaseComponent()
    {
        var type = GetType();
        if (type.IsGenericType)
        {
            ComponentName = type.Name.Split("`").First().FromPascalCaseToKebabCase();
        }
        else
        {
            ComponentName = type.Name.FromPascalCaseToKebabCase();
        }
    }
}
