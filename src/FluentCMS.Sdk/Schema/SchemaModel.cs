using System.Collections.ObjectModel;

namespace FluentCMS.Schema;

/// <summary>
/// The immutable description of every content type, component and block. The attributes, the fluent builder
/// and a <c>schema.json</c> file all produce it. Types are ordered by name and fields by declaration.
/// </summary>
public sealed class SchemaModel
{
    private readonly Dictionary<string, SchemaType> _contentTypes;
    private readonly Dictionary<string, SchemaType> _components;
    private readonly Dictionary<string, SchemaType> _blocks;

    internal SchemaModel(IEnumerable<SchemaType> types, IFieldTypeRegistry fieldTypes)
    {
        FieldTypes = fieldTypes;
        var ordered = types.OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

        ContentTypes = Select(ordered, SchemaTypeKind.Collection, SchemaTypeKind.Singleton);
        Components = Select(ordered, SchemaTypeKind.Component);
        Blocks = Select(ordered, SchemaTypeKind.Block);

        _contentTypes = ContentTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
        _components = Components.ToDictionary(type => type.Name, StringComparer.Ordinal);
        _blocks = Blocks.ToDictionary(type => type.Name, StringComparer.Ordinal);
    }

    // The registry the schema was validated with, for the engine; it is not part of the schema itself.
    internal IFieldTypeRegistry FieldTypes { get; }

    /// <summary>The collections and singletons, ordered by name.</summary>
    public IReadOnlyList<SchemaType> ContentTypes { get; }

    /// <summary>The components, ordered by name.</summary>
    public IReadOnlyList<SchemaType> Components { get; }

    /// <summary>The blocks, ordered by name.</summary>
    public IReadOnlyList<SchemaType> Blocks { get; }

    /// <summary>Finds a collection or singleton by name.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <returns>The type, or <see langword="null"/> if there is none with that name.</returns>
    public SchemaType? FindContentType(string name) => _contentTypes.GetValueOrDefault(name);

    /// <summary>Finds a collection or singleton by the class it was read from.</summary>
    /// <param name="clrType">The class.</param>
    /// <returns>The type, or <see langword="null"/> if none was read from that class.</returns>
    public SchemaType? FindContentType(Type clrType) => ContentTypes.FirstOrDefault(type => type.ClrType == clrType);

    /// <summary>Finds a component by name.</summary>
    /// <param name="name">The component name, in lower kebab-case.</param>
    /// <returns>The component, or <see langword="null"/> if there is none with that name.</returns>
    public SchemaType? FindComponent(string name) => _components.GetValueOrDefault(name);

    /// <summary>Finds a block by name.</summary>
    /// <param name="name">The block name, in lower kebab-case.</param>
    /// <returns>The block, or <see langword="null"/> if there is none with that name.</returns>
    public SchemaType? FindBlock(string name) => _blocks.GetValueOrDefault(name);

    private static ReadOnlyCollection<SchemaType> Select(List<SchemaType> types, params SchemaTypeKind[] kinds) =>
        types.Where(type => kinds.Contains(type.Kind)).ToList().AsReadOnly();
}
