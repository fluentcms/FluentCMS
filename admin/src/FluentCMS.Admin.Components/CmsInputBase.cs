using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Components;

/// <summary>
/// The parameters every input of the wrapped set shares: a visible label, help and error text that the input is described by,
/// and the required and invalid states, all exposed to assistive technology (spec 4.18).
/// </summary>
/// <typeparam name="TValue">The type of the bound value.</typeparam>
public abstract class CmsInputBase<TValue> : ComponentBase
{
    private readonly string _generatedId = CmsIds.Next();
    private TValue? _shownValue;

    /// <summary>Gets or sets the visible label of the input.</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the bound value.</summary>
    [Parameter]
    public TValue? Value { get; set; }

    /// <summary>Gets or sets the callback raised when the user changes the value.</summary>
    [Parameter]
    public EventCallback<TValue?> ValueChanged { get; set; }

    /// <summary>Gets or sets help text shown below the input while it has no error.</summary>
    [Parameter]
    public string? Help { get; set; }

    /// <summary>Gets or sets the error text; when set, the input is marked invalid and the error replaces the help.</summary>
    [Parameter]
    public string? Error { get; set; }

    /// <summary>Gets or sets whether a value is required.</summary>
    [Parameter]
    public bool Required { get; set; }

    /// <summary>Gets or sets whether the input is disabled.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets whether the value is shown and cannot be changed. Unlike <see cref="Disabled"/>, a read-only input stays
    /// in the tab order and is read by assistive technology; an input whose element has no read-only state is disabled.
    /// </summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Gets or sets the callback raised when focus leaves the input, such as for validation as the user leaves a field.</summary>
    [Parameter]
    public EventCallback OnBlur { get; set; }

    /// <summary>
    /// Gets or sets the language of the value, as <c>lang</c> on the input element only, so that the label, help and error
    /// stay in the interface's language; the content locale of a field of an entry. Used by the inputs that take text.
    /// </summary>
    [Parameter]
    public string? Lang { get; set; }

    /// <summary>Gets or sets the direction of the value, <c>ltr</c> or <c>rtl</c>, as <c>dir</c> on the input element only, like <see cref="Lang"/>.</summary>
    [Parameter]
    public string? Dir { get; set; }

    /// <summary>Gets or sets the identifier of the input element; one is generated when it is not set.</summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>Gets the identifier of the input element.</summary>
    protected string InputId => Id ?? _generatedId;

    /// <summary>Gets or sets the reference of the input element, which <see cref="FocusAsync"/> focuses.</summary>
    protected ElementReference Element { get; set; }

    /// <summary>
    /// Gets or sets an error of the input itself, such as text that does not read as a date; shown while <see cref="Error"/>
    /// is not set.
    /// </summary>
    protected string? InputError { get; set; }

    /// <summary>Gets the identifier of another element that describes the input, such as a length counter, or <see langword="null"/>.</summary>
    protected virtual string? AdditionalDescriptionId => null;

    /// <summary>Gets whether the input has an error.</summary>
    protected bool Invalid => !string.IsNullOrEmpty(ShownError);

    /// <summary>Gets the text that describes the input: the error when there is one, otherwise the help.</summary>
    protected string? Message => Invalid ? ShownError : Help;

    private string? ShownError => string.IsNullOrEmpty(Error) ? InputError : Error;

    /// <summary>Gets the identifier of the element that holds <see cref="Message"/>, or <see langword="null"/> without one.</summary>
    protected string? MessageId => string.IsNullOrEmpty(Message) ? null : InputId + "-message";

    /// <summary>Gets the visible label, tied to the input element, with a marker when a value is required.</summary>
    protected RenderFragment LabelTemplate => builder =>
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", "cms-field-label");
        builder.AddAttribute(2, "for", InputId);
        builder.AddContent(3, Label);
        if (Required)
        {
            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", "cms-field-required");
            builder.AddAttribute(6, "aria-hidden", "true");
            builder.AddContent(7, " *");
            builder.CloseElement();
        }

        builder.CloseElement();
    };

    /// <summary>Gets the element that shows <see cref="Message"/>, which the input is described by.</summary>
    protected RenderFragment? MessageTemplate => MessageId is not { } id ? null : builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "id", id);
        builder.AddAttribute(2, "class", Invalid ? "cms-field-message cms-field-message-error" : "cms-field-message");
        builder.AddContent(3, Message);
        builder.CloseElement();
    };

    /// <summary>Gets the ARIA attributes of the input element.</summary>
    protected IReadOnlyDictionary<string, object> AriaAttributes
    {
        get
        {
            var attributes = new Dictionary<string, object>(StringComparer.Ordinal);

            var describedBy = string.Join(' ', new[] { MessageId, AdditionalDescriptionId }.Where(id => id is not null));
            if (describedBy.Length > 0)
            {
                attributes["aria-describedby"] = describedBy;
            }

            if (Required)
            {
                attributes["aria-required"] = "true";
            }

            if (Invalid)
            {
                attributes["aria-invalid"] = "true";
            }

            return attributes;
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        CmsGuard.Named(Label, GetType().Name, nameof(Label));

        // A value set from outside, such as a restored draft, replaces the text the input could not read.
        if (!EqualityComparer<TValue?>.Default.Equals(Value, _shownValue))
        {
            InputError = null;
        }

        _shownValue = Value;
    }

    /// <summary>Moves focus to the input element, which scrolls it into view; does nothing before it is rendered.</summary>
    /// <returns>A task that completes when focus has moved.</returns>
    public ValueTask FocusAsync() => Element.Context is null ? ValueTask.CompletedTask : Element.FocusAsync();

    /// <summary>Sets <see cref="Value"/> and raises <see cref="ValueChanged"/>; does nothing while the input is read-only.</summary>
    /// <param name="value">The new value.</param>
    /// <returns>A task that completes when the callback has run.</returns>
    protected Task SetValueAsync(TValue? value)
    {
        if (ReadOnly)
        {
            return Task.CompletedTask;
        }

        Value = value;
        return ValueChanged.InvokeAsync(value);
    }
}
