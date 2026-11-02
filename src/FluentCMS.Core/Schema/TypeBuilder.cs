using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class TypeBuilder(TypeDraft draft) : ITypeBuilder
{
    public ITypeBuilder Localized(bool localized = true)
    {
        draft.Localized = localized;
        return this;
    }

    public ITypeBuilder Scope(ContentScope scope)
    {
        if (scope != ContentScope.Site && !draft.IsContentType)
        {
            throw new ArgumentException($"{draft.Kind.ToString().ToLowerInvariant()} '{draft.Name}' cannot have a scope; only collections and singletons do.", nameof(scope));
        }

        draft.Scope = scope;
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
