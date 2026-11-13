using FluentCMS.Generators.Emit;
using FluentCMS.Schema;

namespace FluentCMS.Cli.Generate;

internal sealed record GeneratedFile(string Name, string Text);

internal sealed record GenerateDiagnostic(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

internal sealed record GeneratedCode(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<GenerateDiagnostic> Diagnostics);

// What both emitters write of a field's type, in a comment and in a warning: the field type only when it passes the check
// of ClientSchema.Read, so that no other text of a snapshot reaches either.
internal static class FieldTypeText
{
    // "text, multiple, localizable".
    public static string Traits(SchemaField field) => string.Join(", ", new[]
    {
        ClientSchema.IsFieldTypeName(field.FieldType) ? field.FieldType : null,
        field.Multiple ? "multiple" : null,
        field.Localizable ? "localizable" : null,
    }.OfType<string>());

    // "the field type 'geo'".
    public static string Of(SchemaField field) => ClientSchema.IsFieldTypeName(field.FieldType) ? $"the field type '{field.FieldType}'" : "its field type";
}

// The class names of the types of a schema, shared by both emitters so that a type has one name in C# and in TypeScript:
// the name in PascalCase, with Component or Block after a component or a block type, whose names may be those of a
// collection. A name that gives no identifier or that another type took gets nothing, with FCMS3376.
internal sealed class TypeNames
{
    private readonly Dictionary<(SchemaTypeKind Kind, string Name), string> _names = [];

    public TypeNames(SchemaModel schema, IEnumerable<string> reserved, List<GenerateDiagnostic> diagnostics)
    {
        var taken = new HashSet<string>(reserved, StringComparer.Ordinal);
        foreach (var type in schema.ContentTypes.Concat(schema.Components).Concat(schema.Blocks))
        {
            var suffix = type.Kind switch
            {
                SchemaTypeKind.Component => "Component",
                SchemaTypeKind.Block => "Block",
                _ => "",
            };

            var name = FieldsEmitter.Pascal(type.Name) + suffix;
            if (!FieldsEmitter.IsTypeName(type.Name) || !taken.Add(name))
            {
                diagnostics.Add(new(CliErrorCodes.NameNotGenerated, $"The {Label(type.Kind)} '{type.Name}' gets no type, because its name gives no identifier or {name} is taken."));
                continue;
            }

            _names[(Group(type.Kind), type.Name)] = name;
        }
    }

    public IEnumerable<string> All => _names.Values;

    public string? Of(SchemaType type) => _names.GetValueOrDefault((Group(type.Kind), type.Name));

    public string? ContentType(string name) => _names.GetValueOrDefault((SchemaTypeKind.Collection, name));

    public string? Component(string name) => _names.GetValueOrDefault((SchemaTypeKind.Component, name));

    public string? Block(string name) => _names.GetValueOrDefault((SchemaTypeKind.Block, name));

    public static string Label(SchemaTypeKind kind) => kind switch
    {
        SchemaTypeKind.Collection => "collection",
        SchemaTypeKind.Singleton => "singleton",
        SchemaTypeKind.Component => "component",
        _ => "block type",
    };

    // Collections and singletons share one namespace of names (spec 4.2).
    private static SchemaTypeKind Group(SchemaTypeKind kind) => kind == SchemaTypeKind.Singleton ? SchemaTypeKind.Collection : kind;
}
