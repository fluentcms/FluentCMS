using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// A schema file as the schema tools read it: built and validated, so that a file that is not in the canonical form still
// compares by what it says.
internal static class SchemaFiles
{
    public static SchemaModel Read(string path, IFieldTypeRegistry fieldTypes) => new SchemaBuilder(fieldTypes).LoadSchemaFile(path).Build();

    // A failure to read the file at all, as opposed to a file with errors, which SchemaValidationException carries.
    public static bool IsUnreadable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

// The field types of a process that runs no host, such as the fluentcms tool: core's, and for any other name one that it
// cannot check, since the plugin that registers it is not there. The names it stood in for are kept, so that the tool can
// say which fields a host still has to check.
internal sealed class UncheckedFieldTypes : IFieldTypeRegistry
{
    private readonly SortedSet<string> _unchecked = new(StringComparer.Ordinal);

    public IReadOnlyList<IFieldType> All => FieldTypeRegistry.Core.All;

    public IReadOnlyCollection<string> Unchecked => _unchecked;

    public IFieldType? Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (FieldTypeRegistry.Core.Find(name) is { } known)
        {
            return known;
        }

        _unchecked.Add(name);
        return new UncheckedFieldType(name);
    }

    private sealed class UncheckedFieldType(string name) : IFieldType
    {
        public string Name { get; } = name;

        public IReadOnlyList<Type> ClrTypes => [];

        public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

        public IReadOnlyList<FieldSettingDescriptor> Settings => [];

        public bool AcceptsAnySetting => true;

        public bool SupportsMultiple => true;

        public bool Sortable(SchemaField field) => false;

        public bool Indexable(SchemaField field) => true;

        public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Json;

        public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
        {
            result = value;
            message = null;
            return true;
        }

        public JsonObject OpenApiSchema(SchemaField field) => [];
    }
}
