using System.Collections.Frozen;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal abstract class CoreFieldType(string name, Type[] clrTypes, FilterOperator[] filterOperators, bool sortable, bool indexable, params FieldSettingDescriptor[] settings) : IFieldType
{
    public string Name { get; } = name;

    public IReadOnlyList<Type> ClrTypes { get; } = clrTypes;

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = filterOperators.ToFrozenSet();

    public bool Sortable { get; } = sortable;

    public bool Indexable { get; } = indexable;

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = settings;

    public virtual FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

    public abstract bool TryCoerce(object? value, SchemaField field, out object? result, out string? message);

    public abstract JsonObject OpenApiSchema(SchemaField field);

    protected static bool Ok(object? value, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    protected static bool Fail(string problem, out object? result, out string? message)
    {
        result = null;
        message = problem;
        return false;
    }

    protected static bool Expected(string what, object? raw, out object? result, out string? message) =>
        Fail($"expected {what} but got {RawValue.Describe(raw)}", out result, out message);
}
