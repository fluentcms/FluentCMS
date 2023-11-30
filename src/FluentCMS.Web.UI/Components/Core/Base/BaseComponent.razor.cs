// Base component

using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core.Base;

public class BaseComponent: ComponentBase {
    [Parameter]
    public RenderFragment ChildContent {get; set;}

    [Parameter]
    public string Class {get; set;} = "";

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? RestProps { get; set; }

}