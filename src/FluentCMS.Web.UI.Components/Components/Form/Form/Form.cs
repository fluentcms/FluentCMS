using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public abstract class FormElements
{
    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public string? Hint { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? LabelFragment { get; set; }

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Required { get; set; }
}

public abstract class FormElementsValuable<T> : FormElements
{
    protected T? CurrentValue { get; set; }

    [Parameter]
    public EventCallback<T> OnChange { get; set; }

    [Parameter]
    public T? Value
    {
        get => CurrentValue;
        set
        {
            if (value != null && !value.Equals(CurrentValue))
            {
                CurrentValue = value;
                ValueChanged.InvokeAsync(value);
                OnChange.InvokeAsync(value);
            }
        }
    }

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }
}

public abstract class FormCheckboxesType<T> : FormElementsValuable<T>
{
}

public abstract class FormInputsType<T> : FormElementsValuable<T>
{
    [Parameter]
    public IconName? IconEnd { get; set; }

    [Parameter]
    public IconName? IconStart { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }
}

public enum FormFieldState
{
    Default,
    Invalid,
    Valid,
}
