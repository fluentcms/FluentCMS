namespace FluentCMS.Generators.Emit;

// What the emitter reads: the types, their fields and the kinds of the fields, by their schema names. The source
// generator fills it from Roslyn symbols and the tool from a schema snapshot, so both emit the same code.
internal enum BuilderKind
{
    Collection,
    Singleton,
    Component,
    Block,
}

// FieldType is the name of the field type in the schema, such as text or media. Component names the component of a
// component field and Blocks the block types of a blocks field.
internal sealed record FieldDescription(string Name, string FieldType, bool Multiple, string? Component, EquatableArray<string> Blocks);

// Namespace is the C# namespace the builder is emitted into, empty for the global namespace.
internal sealed record TypeDescription(string Name, BuilderKind Kind, string Namespace, EquatableArray<FieldDescription> Fields);

// FieldTypesNamespace holds the field types the builders return: FluentCMS.Content in a host, another in a client.
internal sealed record EmitOptions(string FieldTypesNamespace)
{
    public static EmitOptions Sdk { get; } = new("FluentCMS.Content");
}

internal sealed record EmittedSource(string HintName, string Text);

internal sealed record EmitDiagnostic(string Code, string Message, BuilderKind Kind, string TypeName, string? FieldName);

internal sealed record EmitResult(EquatableArray<EmittedSource> Sources, EquatableArray<EmitDiagnostic> Diagnostics);

internal static class EmitCodes
{
    // A type gets no builder: its name gives no C# class name, or another type's builder has that name.
    public const string TypeSkipped = "FCMS0100";

    // A field gets no builder: its field type is not one the emitter knows, such as a plugin type other than media.
    public const string UnknownFieldType = "FCMS0101";

    // A field or a block type gets no member: its member name is taken in the builder.
    public const string MemberNameTaken = "FCMS0102";

    // A field or a block type gets no member: the component or block it names has no builder in the same run.
    public const string MissingBuilder = "FCMS0103";
}
