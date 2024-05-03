using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public abstract class BaseComponent : ComponentBase
{
    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public string Class { get; set; } = string.Empty;

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> AdditionalAttributes { get; set; } = default!;

    public string ComponentName { get; } = string.Empty;

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

    public override Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        if (Visible)
            return base.SetParametersAsync(parameters);

        return Task.CompletedTask;
    }

    // Prevents rendering if StateHasChanged is called
    protected override bool ShouldRender()
    {
        return Visible;
    }
}
