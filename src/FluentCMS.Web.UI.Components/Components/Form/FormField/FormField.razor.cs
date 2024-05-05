namespace FluentCMS.Web.UI.Components;

public partial class FormField : IDisposable
{
    [Parameter]
    public FormFieldAppearance Appearance { get; set; }

    [Parameter]
    public FormElements? Field { get; set; }

    [CSSProperty]
    public bool HasIconEnd { get => IconEnd != null; }

    [CSSProperty]
    public bool HasIconStart { get => IconStart != null; }

    [CSSProperty]
    public FormFieldState State { get; set; }

    [CascadingParameter]
    EditContext? Context { get; set; }

    IconName? IconEnd
    {
        get
        {
            return (IconName?)Field?.GetType().GetProperty("IconEnd")?.GetValue(Field);
        }
    }

    IconName? IconStart
    {
        get
        {
            return (IconName?)Field?.GetType().GetProperty("IconStart")?.GetValue(Field);
        }
    }

    bool IsInlineLabel
    {
        get => Appearance == FormFieldAppearance.Checkbox || Appearance == FormFieldAppearance.Radio || Appearance == FormFieldAppearance.Switch;
    }

    IEnumerable<string> Errors
    {
        get
        {
            if (Context == null || Identifier == null)
            {
                return new List<string> { };
            }

            return Context.GetValidationMessages(Identifier.GetValueOrDefault());
        }
    }

    FieldIdentifier? Identifier
    {
        get
        {
            if (Context == null || Field?.Name == null)
            {
                return null;
            }

            return Context.Field(Field.Name);
        }
    }

    void OnValidationRequested(object sender, ValidationRequestedEventArgs args)
    {
        if (Context == null || Identifier == null) return;

        var IsValid = Context.IsValid(Identifier.GetValueOrDefault());

        State = IsValid ? FormFieldState.Default : FormFieldState.Invalid;
    }

    public void Dispose()
    {
        if (Context == null) return;

        Context.OnValidationRequested -= OnValidationRequested;
    }

    protected override void OnInitialized()
    {
        if (Context == null) return;

        Context.OnValidationRequested += OnValidationRequested;
    }
}
