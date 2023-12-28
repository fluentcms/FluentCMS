using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core;

public abstract class InputBaseComponent<T> : BaseComponent
{
    [Parameter]
    public T? Value
    {
        get => CurrentValue;
        set
        {
            if (value != null && !value.Equals(CurrentValue))
            {
                ValueChanged.InvokeAsync(value);
                CurrentValue = value;
            }
        }
    }

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    [Parameter]
    public bool Required { get; set; } = false;

    [Parameter]
    public bool Disabled { get; set; } = false;

    [Parameter]
    public string Label { get; set; }

    [Parameter]
    public string Name { get; set; }

    [Parameter]
    public string State { get; set; } = "FormFieldStateEnum.Default";

    // current value to invoke ValueChanged
    protected T? CurrentValue { get; set; }
}
