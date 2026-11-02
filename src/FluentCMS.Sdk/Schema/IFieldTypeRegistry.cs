namespace FluentCMS.Schema;

/// <summary>The field types a host knows: the ones core registers and the ones plugins add.</summary>
public interface IFieldTypeRegistry
{
    /// <summary>Every field type, ordered by name.</summary>
    IReadOnlyList<IFieldType> All { get; }

    /// <summary>Finds a field type by name.</summary>
    /// <param name="name">The name as used in <see cref="SchemaField.FieldType"/>, such as <c>richText</c>.</param>
    /// <returns>The field type, or <see langword="null"/> if none is registered under that name.</returns>
    IFieldType? Find(string name);
}
