using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class TypeDraft(string name, SchemaTypeKind kind, Type? clrType)
{
    public string Name { get; } = name;

    public SchemaTypeKind Kind { get; } = kind;

    public Type? ClrType { get; } = clrType;

    public bool Localized { get; set; }

    public ContentScope Scope { get; set; }

    public UnpublishedReferencePolicy UnpublishedReferences { get; set; }

    public int MaxVersions { get; set; } = PublishPolicyAttribute.DefaultMaxVersions;

    public List<FieldDraft> Fields { get; } = [];

    public bool IsContentType => Kind is SchemaTypeKind.Collection or SchemaTypeKind.Singleton;
}
