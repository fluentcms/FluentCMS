using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class TypeBuilder(TypeDraft draft) : ITypeBuilder
{
    public ITypeBuilder Localized(bool localized)
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

    public ITypeBuilder PublishPolicy(PublishPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!draft.IsContentType)
        {
            throw new ArgumentException($"{draft.Kind.ToString().ToLowerInvariant()} '{draft.Name}' is not published; only collections and singletons are.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxVersions, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TrashRetentionDays, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TrashRetentionDays, PublishPolicyAttribute.MaxTrashRetentionDays, nameof(options));

        draft.UnpublishedReferences = options.UnpublishedReferences;
        draft.MaxVersions = options.MaxVersions;
        draft.TrashRetentionDays = options.TrashRetentionDays;
        return this;
    }

    public ITypeBuilder Field(string name, string fieldType, Action<IFieldBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(fieldType);
        ArgumentNullException.ThrowIfNull(configure);

        var field = new FieldDraft(name, fieldType);
        draft.Fields.Add(field);
        configure(new FieldBuilder(field));
        return this;
    }
}
