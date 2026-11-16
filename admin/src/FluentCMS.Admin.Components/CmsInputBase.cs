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

    /// <summary>Gets or sets the identifier of the input element; one is generated when it is not set.</summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>Gets the identifier of the input element.</summary>
    protected string InputId => Id ?? _generatedId;

    /// <summary>Gets whether the input has an error.</summary>
    protected bool Invalid => !string.IsNullOrEmpty(Error);

    /// <summary>Gets the text that describes the input: the error when there is one, otherwise the help.</summary>
    protected string? Message => Invalid ? Error : Help;

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

            if (MessageId is not null)
            {
                attributes["aria-describedby"] = MessageId;
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

    /// <summary>Sets <see cref="Value"/> and raises <see cref="ValueChanged"/>.</summary>
    /// <param name="value">The new value.</param>
    /// <returns>A task that completes when the callback has run.</returns>
    protected Task SetValueAsync(TValue? value)
    {
        Value = value;
        return ValueChanged.InvokeAsync(value);
    }
}
