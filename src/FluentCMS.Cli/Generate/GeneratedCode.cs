using FluentCMS.Core.Schema.FieldTypes;
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
        FieldTypeNames.IsValid(field.FieldType) ? field.FieldType : null,
        field.Multiple ? "multiple" : null,
        field.Localizable ? "localizable" : null,
    }.OfType<string>());

    // "the field type 'geo'".
    public static string Of(SchemaField field) => FieldTypeNames.IsValid(field.FieldType) ? $"the field type '{field.FieldType}'" : "its field type";
}

// The class names of the types of a schema, shared by both emitters so that a type has one name in C# and in TypeScript:
// the name in PascalCase, with Component or Block after a component or a block type, whose names may be those of a
// collection. A name that gives no identifier or that another type took gets nothing, with FCMS3376. A type takes its
// name, against the other types and against a reserved name of a file, such as a builder's, in any case, since the class
// of a type is a file of its name and foo-bar and foobar would be FooBar.g.cs and Foobar.g.cs, one file where a file
// system ignores case. A reserved name that starts in lower case is an identifier of the TypeScript module, such as
// createClient, which no file is named after: it is taken as written, so the type create-client keeps CreateClient.
internal sealed class TypeNames
{
    private readonly Dictionary<(SchemaTypeKind Kind, string Name), string> _names = [];

    public TypeNames(SchemaModel schema, IEnumerable<string> reserved, List<GenerateDiagnostic> diagnostics)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in reserved)
        {
            (name.Length > 0 && char.IsAsciiLetterLower(name[0]) ? identifiers : taken).Add(name);
        }

        foreach (var type in schema.ContentTypes.Concat(schema.Components).Concat(schema.Blocks))
        {
            var suffix = type.Kind switch
            {
                SchemaTypeKind.Component => "Component",
                SchemaTypeKind.Block => "Block",
                _ => "",
            };

            var name = FieldsEmitter.Pascal(type.Name) + suffix;
            if (!FieldsEmitter.IsTypeName(type.Name) || identifiers.Contains(name) || !taken.Add(name))
            {
                diagnostics.Add(new(CliErrorCodes.NameNotGenerated, $"The {Label(type.Kind)} '{type.Name}' gets no type, because its name gives no identifier or {name} is taken, in this case or another."));
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
