using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class TypeBuilder(TypeDraft draft) : ITypeBuilder
{
    public ITypeBuilder Localized(bool localized = true)
    {
        draft.Localized = localized;
        return this;
    }

    public ITypeBuilder Field(string name, string fieldType, Action<IFieldBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(fieldType);

        var field = new FieldDraft(name, fieldType);
        draft.Fields.Add(field);
        configure?.Invoke(new FieldBuilder(field));
        return this;
    }
}
