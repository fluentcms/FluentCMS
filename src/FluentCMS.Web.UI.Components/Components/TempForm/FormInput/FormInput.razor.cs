using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class FormInput
{
    [Parameter]
    public int Cols { get; set; } = 12;

    public EventCallback<ChangeEventArgs> OnChange()
    {
        return EventCallback.Factory.CreateBinder<string?>(this, __value => CurrentValueAsString = __value, CurrentValueAsString);
    }

    public string CssClasses
    {
        get => this.CssClass + " " + BaseInputHelper.GetClasses<string>(this);
    }

    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }

}
