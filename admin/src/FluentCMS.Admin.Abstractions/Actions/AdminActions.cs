using System.Reflection;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;

namespace FluentCMS.Admin.Actions;

/// <summary>
/// What the actions of an admin half share. An action is a class the provider of its half creates, so its constructor
/// takes the half's services and the admin's.
/// </summary>
public abstract class AdminActionBase
{
    /// <summary>Gets the text of the action's button or menu item, from the half's resources.</summary>
    public abstract string Label { get; }

    /// <summary>Gets the name of the action's icon, one of the names of the wrapped component set, or <see langword="null"/> for none.</summary>
    public virtual string? Icon => null;
}

/// <summary>
/// The base class of an action on one entry, declared with <see cref="EntryActionAttribute"/>. The admin reads the entry
/// again once the action has run, and shows an <see cref="ApiProblemException"/> the action throws.
/// </summary>
public abstract class EntryActionBase : AdminActionBase
{
    /// <summary>Gets the content types the action is offered on, from its attribute; every type when empty.</summary>
    public IReadOnlyList<string> ContentTypes => GetType().GetCustomAttribute<EntryActionAttribute>()?.ContentTypes ?? [];

    /// <summary>Whether the action is offered on an entry of one of its content types, such as on a draft only.</summary>
    /// <param name="context">The entry.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the action is offered.</returns>
    public virtual ValueTask<bool> AppliesToAsync(EntryActionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    /// <summary>The text the admin asks the user to confirm before the action runs.</summary>
    /// <param name="context">The entry.</param>
    /// <returns>The text, or <see langword="null"/> to run without asking.</returns>
    public virtual string? ConfirmationText(EntryActionContext context) => null;

    /// <summary>Runs the action.</summary>
    /// <param name="context">The entry.</param>
    /// <param name="cancellationToken">Cancels the action.</param>
    /// <returns>A task that completes when the action has run.</returns>
    public abstract Task RunAsync(EntryActionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The base class of an action on the selection of a list, declared with <see cref="BulkActionAttribute"/>. The admin
/// confirms it, shows the progress it reports, and reads the list again once it has run.
/// </summary>
public abstract class BulkActionBase : AdminActionBase
{
    /// <summary>Gets the content types the action is offered on, from its attribute; every type when empty.</summary>
    public IReadOnlyList<string> ContentTypes => GetType().GetCustomAttribute<BulkActionAttribute>()?.ContentTypes ?? [];

    /// <summary>The text the admin asks the user to confirm before the action runs.</summary>
    /// <param name="context">The selection.</param>
    /// <returns>The text, or <see langword="null"/> for the admin's own, which names the action, the type and the count.</returns>
    public virtual string? ConfirmationText(BulkActionContext context) => null;

    /// <summary>Runs the action, and reports each entry as it is done.</summary>
    /// <param name="context">The selection.</param>
    /// <param name="progress">The progress the admin shows, started for the entries of the selection.</param>
    /// <param name="cancellationToken">The token of <paramref name="progress"/>, cancelled when the user cancels; the action stops after the entries it is working on.</param>
    /// <returns>A task that completes when the action has run or stopped.</returns>
    public abstract Task RunAsync(BulkActionContext context, IBulkProgress progress, CancellationToken cancellationToken);
}

/// <summary>
/// The base class of an action on a field of a form, declared with <see cref="FieldActionAttribute"/>. It changes the
/// form's draft only, through its result, and never saves.
/// </summary>
public abstract class FieldActionBase : AdminActionBase
{
    /// <summary>Gets the field types the action is offered on, from its attribute; every type when empty.</summary>
    public IReadOnlyList<string> FieldTypes => GetType().GetCustomAttribute<FieldActionAttribute>()?.FieldTypes ?? [];

    /// <summary>Whether the action is offered on a field of one of its field types, such as on the fields a site opted in.</summary>
    /// <param name="context">The field.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the action is offered.</returns>
    public virtual ValueTask<bool> AppliesToAsync(FieldActionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    /// <summary>The text the admin asks the user to confirm before the action runs.</summary>
    /// <param name="context">The field.</param>
    /// <returns>The text, or <see langword="null"/> to run without asking.</returns>
    public virtual string? ConfirmationText(FieldActionContext context) => null;

    /// <summary>Runs the action.</summary>
    /// <param name="context">The field.</param>
    /// <param name="cancellationToken">Cancels the action.</param>
    /// <returns>What the form does with the field's value.</returns>
    public abstract Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken);
}

/// <summary>The entry an <see cref="EntryActionBase"/> acts on.</summary>
public sealed class EntryActionContext
{
    /// <summary>Gets the content type of the entry.</summary>
    public required SchemaTypeDescription Type { get; init; }

    /// <summary>Gets the entry, as the form or the list read it.</summary>
    public required ContentEntry Entry { get; init; }

    /// <summary>Gets the content locale of the view, or <see langword="null"/> for a type that is not localized.</summary>
    public string? ContentLocale { get; init; }
}

/// <summary>The selection a <see cref="BulkActionBase"/> acts on.</summary>
public sealed class BulkActionContext
{
    /// <summary>Gets the content type of the list.</summary>
    public required SchemaTypeDescription Type { get; init; }

    /// <summary>Gets the ids of the selected entries.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }

    /// <summary>Gets the content locale of the view, or <see langword="null"/> for a type that is not localized.</summary>
    public string? ContentLocale { get; init; }
}

/// <summary>The field a <see cref="FieldActionBase"/> acts on.</summary>
public sealed class FieldActionContext
{
    /// <summary>Gets the content type of the form.</summary>
    public required SchemaTypeDescription Type { get; init; }

    /// <summary>Gets the field.</summary>
    public required SchemaFieldDescription Field { get; init; }

    /// <summary>Gets the value of the field in the form's draft. The node is the form's own: an action must not change it.</summary>
    public JsonNode? Value { get; init; }

    /// <summary>Gets the values of every field of the form's draft by field name, which an action reads and must not change.</summary>
    public required IReadOnlyDictionary<string, JsonNode?> Document { get; init; }

    /// <summary>Gets the id of the entry, or <see langword="null"/> for one that is not created yet.</summary>
    public Guid? EntryId { get; init; }

    /// <summary>Gets the content locale of the form, or <see langword="null"/> for a type that is not localized.</summary>
    public string? ContentLocale { get; init; }
}

/// <summary>What a form does with a field's value after a <see cref="FieldActionBase"/> has run.</summary>
public sealed class FieldActionResult
{
    private FieldActionResult(bool replaces, JsonNode? value)
    {
        Replaces = replaces;
        Value = value;
    }

    /// <summary>Gets the result that leaves the value as it is.</summary>
    public static FieldActionResult Unchanged { get; } = new(false, null);

    /// <summary>Gets whether the form takes <see cref="Value"/> as the field's value.</summary>
    public bool Replaces { get; }

    /// <summary>Gets the new value of the field, when <see cref="Replaces"/> is set.</summary>
    public JsonNode? Value { get; }

    /// <summary>The result that puts a value into the form's draft.</summary>
    /// <param name="value">The new value, a node of no document, or <see langword="null"/> to clear the field.</param>
    /// <returns>The result.</returns>
    public static FieldActionResult Replace(JsonNode? value) => new(true, value);
}
