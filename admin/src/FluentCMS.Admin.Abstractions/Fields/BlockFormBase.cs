using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Fields;

/// <summary>
/// The generated form of one block or component: its fields, each with the editor of its field type. A field editor that holds
/// blocks of its own, such as the inline blocks of rich text, renders the admin's form through <see cref="BlockFormRegistration"/>.
/// </summary>
public abstract class BlockFormBase : ComponentBase
{
    /// <summary>Gets or sets the block or component type, from the schema's <c>blocks</c> or <c>components</c>.</summary>
    [Parameter, EditorRequired]
    public SchemaTypeDescription BlockType { get; set; } = null!;

    /// <summary>Gets or sets the block's value: its fields, and a block's <c>type</c> and <c>id</c>, which the form keeps as they are.</summary>
    [Parameter]
    public JsonObject? Value { get; set; }

    /// <summary>Gets or sets the callback the form raises with the block's new value, a new object, when the user changes a field.</summary>
    [Parameter]
    public EventCallback<JsonObject> ValueChanged { get; set; }

    /// <summary>
    /// Gets or sets where the block is in the entry, as the server's error paths name it, such as <c>sections[0]</c>; the errors
    /// at <c>sections[0].heading</c> are then shown on the field <c>heading</c>.
    /// </summary>
    [Parameter]
    public string Path { get; set; } = "";

    /// <summary>Gets or sets the errors of the field that holds the block; the form shows those at or under <see cref="Path"/>.</summary>
    [Parameter]
    public IReadOnlyList<ApiProblemError> Errors { get; set; } = [];

    /// <summary>Gets or sets whether the user may not change the block.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Gets or sets the language tag of the content locale the form edits.</summary>
    [Parameter]
    public string? ContentLocale { get; set; }

    /// <summary>Gets or sets the text direction of the content locale.</summary>
    [Parameter]
    public string TextDirection { get; set; } = FieldEditorBase.LeftToRight;

    /// <summary>Moves focus to the first field with an error, or to the first field.</summary>
    /// <returns>A task that completes when focus has moved.</returns>
    public virtual ValueTask FocusAsync() => ValueTask.CompletedTask;

    /// <summary>Raises the changes the editors of the block's fields hold back, as <see cref="FieldEditorBase.FlushAsync"/> does.</summary>
    /// <returns>A task that completes when the changes have been raised.</returns>
    public virtual ValueTask FlushAsync() => ValueTask.CompletedTask;
}

/// <summary>The component the admin renders as the form of one block, a <see cref="BlockFormBase"/>, which a field editor can inject.</summary>
public sealed class BlockFormRegistration
{
    /// <summary>Initializes a new instance of the <see cref="BlockFormRegistration"/> class.</summary>
    /// <param name="form">The form, a <see cref="BlockFormBase"/>.</param>
    public BlockFormRegistration([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!typeof(BlockFormBase).IsAssignableFrom(form))
        {
            throw new ArgumentException($"{form} is not a {nameof(BlockFormBase)}.", nameof(form));
        }

        Form = form;
    }

    /// <summary>Gets the form, a <see cref="BlockFormBase"/>.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Form { get; }
}
