using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core;

public abstract class BaseComponent : ComponentBase
{
    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public string Class { get; set; } = "";

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? RestProps { get; set; }
}
