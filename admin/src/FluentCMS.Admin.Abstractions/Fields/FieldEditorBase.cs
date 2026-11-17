using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Fields;

/// <summary>
/// The base class of the editor of a field type in a generated form. The form gives it the field's description, its value,
/// its errors, whether it is read-only, and the content locale with its text direction; the editor raises each change.
/// </summary>
public abstract class FieldEditorBase : ComponentBase
{
    /// <summary>The text direction of a left-to-right locale.</summary>
    public const string LeftToRight = "ltr";

    /// <summary>The text direction of a right-to-left locale.</summary>
    public const string RightToLeft = "rtl";

    /// <summary>Gets or sets the field, as the schema describes it.</summary>
    [Parameter, EditorRequired]
    public SchemaFieldDescription Field { get; set; } = null!;

    /// <summary>Gets or sets the value of the field in the form's document, or <see langword="null"/> when it has none.</summary>
    [Parameter]
    public JsonNode? Value { get; set; }

    /// <summary>Gets or sets the callback the editor raises with the new value when the user changes it.</summary>
    [Parameter]
    public EventCallback<JsonNode?> ValueChanged { get; set; }

    /// <summary>
    /// Gets or sets the errors of the field: those the form found itself as the user left it, such as a text over its maximum
    /// length, and those of the last save whose path is in this field, such as <c>seo.metaTitle</c> for <c>seo</c>.
    /// </summary>
    [Parameter]
    public IReadOnlyList<ApiProblemError> Errors { get; set; } = [];

    /// <summary>Gets or sets whether the user may not change the value.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets whether a value is required, which the form sets from the field's validation and, once conditions are
    /// evaluated, from <c>requiredWhen</c>.
    /// </summary>
    [Parameter]
    public bool Required { get; set; }

    /// <summary>Gets or sets the content locale the form edits, or <see langword="null"/> for the site's default.</summary>
    [Parameter]
    public string? ContentLocale { get; set; }

    /// <summary>Gets or sets the text direction of the content locale: <see cref="LeftToRight"/> or <see cref="RightToLeft"/>.</summary>
    [Parameter]
    public string TextDirection { get; set; } = LeftToRight;

    /// <summary>
    /// Gets or sets the values of every field of the form's document by field name, which an editor reads another field
    /// from, such as a slug its source. The nodes are the form's own: an editor must not change them or add them to another
    /// node, and changes only its own value, through <see cref="ValueChanged"/>.
    /// </summary>
    [Parameter]
    public IReadOnlyDictionary<string, JsonNode?>? Document { get; set; }

    /// <summary>Gets or sets the callback the editor raises when focus leaves its input, such as for validation as the user leaves a field.</summary>
    [Parameter]
    public EventCallback OnBlur { get; set; }

    /// <summary>Gets the label of the field.</summary>
    protected string Label => LabelOf(Field);

    /// <summary>Gets the help text of the field, if it has one.</summary>
    protected string? Help => Field.Display?.Help;

    /// <summary>Gets the placeholder of the field, if it has one.</summary>
    protected string? Placeholder => Field.Display?.Placeholder;


    /// <summary>Gets the messages of <see cref="Errors"/> as one text, or <see langword="null"/> without errors.</summary>
    protected string? ErrorText =>
        Errors.Count == 0 ? null : string.Join(" ", Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)).Distinct());

    /// <summary>
    /// The label of a field: its <c>display.label</c>, or its name made readable, so <c>metaTitle</c> is "Meta title" and
    /// <c>seoURL</c> "Seo url".
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The label.</returns>
    public static string LabelOf(SchemaFieldDescription field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!string.IsNullOrWhiteSpace(field.Display?.Label))
        {
            return field.Display.Label;
        }

        var words = new StringBuilder(field.Name.Length + 4);
        for (var index = 0; index < field.Name.Length; index++)
        {
            var character = field.Name[index];
            if (char.IsUpper(character) && index > 0 && !char.IsUpper(field.Name[index - 1]))
            {
                words.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                words.Append(words.Length == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            }
        }

        return words.ToString();
    }

    /// <summary>Moves focus to the editor's input, which scrolls it into view; the form calls it for the first field with an error.</summary>
    /// <returns>A task that completes when focus has moved.</returns>
    public virtual ValueTask FocusAsync() => ValueTask.CompletedTask;

    /// <summary>Sets <see cref="Value"/> and raises <see cref="ValueChanged"/>.</summary>
    /// <param name="value">The new value.</param>
    /// <returns>A task that completes when the form has taken the change.</returns>
    protected Task ChangeAsync(JsonNode? value)
    {
        Value = value;
        return ValueChanged.InvokeAsync(value);
    }
}
