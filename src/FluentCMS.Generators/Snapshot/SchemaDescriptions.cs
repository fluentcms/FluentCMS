using FluentCMS.Generators.Emit;
using FluentCMS.Schema;

namespace FluentCMS.Generators.Snapshot;

// Describes a SchemaModel for the emitter, as the source generator describes the classes it reads, so that
// `generate csharp` emits from a schema snapshot what the generator emits in a host. Linked by projects that
// reference the SDK; the generator itself cannot.
internal static class SchemaDescriptions
{
    public static IReadOnlyList<TypeDescription> Of(SchemaModel schema, string @namespace)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(@namespace);

        return
        [
            .. schema.ContentTypes.Select(type => Describe(type, type.Kind == SchemaTypeKind.Singleton ? BuilderKind.Singleton : BuilderKind.Collection, @namespace)),
            .. schema.Components.Select(type => Describe(type, BuilderKind.Component, @namespace)),
            .. schema.Blocks.Select(type => Describe(type, BuilderKind.Block, @namespace)),
        ];
    }

    private static TypeDescription Describe(SchemaType type, BuilderKind kind, string @namespace) =>
        new(type.Name, kind, @namespace, type.Fields.Select(Describe).ToEquatableArray());

    private static FieldDescription Describe(SchemaField field) => new(
        field.Name,
        field.FieldType,
        field.Multiple,
        field.Settings.TryGetValue(FieldSettings.Component, out var component) ? component as string : null,
        field.Settings.TryGetValue(FieldSettings.Blocks, out var blocks) && blocks is IEnumerable<string> names ? names.ToEquatableArray() : EquatableArray<string>.Empty);
}
