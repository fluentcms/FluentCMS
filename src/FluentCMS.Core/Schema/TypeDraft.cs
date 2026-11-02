using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class TypeDraft(string name, SchemaTypeKind kind, Type? clrType)
{
    public string Name { get; } = name;

    public SchemaTypeKind Kind { get; } = kind;

    public Type? ClrType { get; } = clrType;

    public bool Localized { get; set; }

    public List<FieldDraft> Fields { get; } = [];

    public bool IsContentType => Kind is SchemaTypeKind.Collection or SchemaTypeKind.Singleton;
}
