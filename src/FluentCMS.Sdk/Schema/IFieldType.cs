using System.Text.Json.Nodes;
using FluentCMS.Content;

namespace FluentCMS.Schema;

/// <summary>
/// Describes one field type: the CLR types it maps to, how it is stored, what a query may do with it, which
/// settings it accepts, how a raw value becomes the value of the field and how it appears in OpenAPI.
/// Rules such as required, length, range, pattern and option membership are not part of a descriptor; the
/// validation pipeline applies them.
/// </summary>
public interface IFieldType
{
    /// <summary>The name used in <see cref="SchemaField.FieldType"/>, in camelCase.</summary>
    string Name { get; }

    /// <summary>The property types a class may use for a field of this type. An open generic type, such as <c>Ref&lt;&gt;</c>, stands for every closed form.</summary>
    IReadOnlyList<Type> ClrTypes { get; }

    /// <summary>The filter operators a query may apply to the type. Whether a field is stored as JSON narrows what the engine can compile.</summary>
    IReadOnlySet<FilterOperator> FilterOperators { get; }

    /// <summary>Whether a query may sort by a field of the type. Never true for a field whose <see cref="StorageKind"/> is <see cref="FieldStorageKind.Json"/>.</summary>
    bool Sortable { get; }

    /// <summary>Whether a field of the type may be marked as indexed.</summary>
    bool Indexable { get; }

    /// <summary>The settings the type accepts in <see cref="SchemaField.Settings"/>.</summary>
    IReadOnlyList<FieldSettingDescriptor> Settings { get; }

    /// <summary>
    /// Whether the type accepts settings that <see cref="Settings"/> does not list, without checking their value.
    /// Core types do not; a plugin type that keeps free-form options can. The default is <see langword="false"/>.
    /// </summary>
    bool AcceptsAnySetting => false;

    /// <summary>How a field of this type is stored; some types depend on the field, such as a component that repeats.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The storage kind.</returns>
    FieldStorageKind StorageKind(SchemaField field);

    /// <summary>
    /// Turns a raw value into the canonical CLR value of the field: a string, a number, a boolean, a
    /// <c>JsonElement</c> or a value that already has the right type. A <see langword="null"/> value is accepted and stays
    /// <see langword="null"/>; whether the field may be empty is a validation rule.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <param name="field">The field.</param>
    /// <param name="result">The canonical value when the call succeeds.</param>
    /// <param name="message">A message that names what is wrong when the call fails.</param>
    /// <returns><see langword="true"/> if the type can hold the value.</returns>
    bool TryCoerce(object? value, SchemaField field, out object? result, out string? message);

    /// <summary>Describes the value of the field as an OpenAPI 3.1 schema, without nullability or the required marker, which belong to the object that holds the field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The schema fragment.</returns>
    JsonObject OpenApiSchema(SchemaField field);
}
