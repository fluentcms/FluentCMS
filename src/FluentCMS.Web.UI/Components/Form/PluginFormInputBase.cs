using Microsoft.AspNetCore.Components.Forms;

namespace FluentCMS.Web.UI.Plugins.Components;

public abstract class FormElement<T> : InputBase<T>
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Required { get; set; }

    [Parameter]
    public bool Visible { get; set; } = true;
}

public abstract class FormElementCheckboxes<T> : FormElement<T>
{
}

public abstract class FormElementInputs<T> : FormElement<T>
{
    [Parameter]
    public string? Placeholder { get; set; }
}
