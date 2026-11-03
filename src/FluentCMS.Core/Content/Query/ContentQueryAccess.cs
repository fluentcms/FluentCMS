using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content.Query;

// The query engine's seam over the evaluator. Outside an operation, or before ForQueryAsync, everything is closed.
internal sealed class ContentQueryAccess(IAuthorizationEvaluator evaluator, IOperationContextAccessor accessor) : IContentQueryAccess
{
    private static readonly Closed Nothing = new();

    public async ValueTask<IContentQueryAccess> ForQueryAsync(SiteId? site, string locale, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Nothing;
        }

        return new Bound(await evaluator.ResolveAsync(context, cancellationToken), context.Site, site, locale);
    }

    public bool CanReadType(SchemaType type) => Nothing.CanReadType(type);

    public bool CanReadDraft(SchemaType type) => Nothing.CanReadDraft(type);

    public bool CanReadField(SchemaType type, string field) => Nothing.CanReadField(type, field);

    public ContentFilter? RowRule(SchemaType type) => Nothing.RowRule(type);

    // A query with no site reads every site of a site-scoped type, so it counts as every site. A global type's entries
    // belong to no site, and the question is then about the site the operation runs in.
    private sealed class Bound(EffectivePermissions permissions, SiteId operationSite, SiteId? site, string locale) : IContentQueryAccess
    {
        private readonly Dictionary<SchemaType, ContentFilter?> _rules = [];

        public bool CanReadType(SchemaType type) => permissions.Allows(type, ContentOperation.Read, Target(type));

        public bool CanReadDraft(SchemaType type) => permissions.CanReadDraft(type, Target(type));

        public bool CanReadField(SchemaType type, string field) => permissions.CanReadField(type, field, Target(type));

        public IReadOnlySet<string>? ReadLocales(SchemaType type) => permissions.ReadLocalesOf(type, Target(type));

        public ContentFilter? RowRule(SchemaType type)
        {
            // One instance per type, because the compiler recognises the rule in a filter by reference.
            if (!_rules.TryGetValue(type, out var rule))
            {
                rule = permissions.RowRuleOf(type, ContentOperation.Read, Target(type));
                _rules[type] = rule;
            }

            return rule;
        }

        private AccessTarget Target(SchemaType type) => new(type.Scope == ContentScope.Global ? operationSite : site, locale);
    }

    internal sealed class Closed : IContentQueryAccess
    {
        public bool CanReadType(SchemaType type) => false;

        public bool CanReadDraft(SchemaType type) => false;

        public bool CanReadField(SchemaType type, string field) => false;

        public ContentFilter? RowRule(SchemaType type) => Authorization.RowRule.NoRows;
    }

    // Another rule on one type, joined to its read rule. One instance per query, since the compiler knows the rule by reference.
    internal sealed class WithRule(IContentQueryAccess inner, SchemaType owner, ContentFilter rule) : IContentQueryAccess
    {
        private ContentFilter? _joined;

        public bool CanReadType(SchemaType type) => inner.CanReadType(type);

        public bool CanReadDraft(SchemaType type) => inner.CanReadDraft(type);

        public bool CanReadField(SchemaType type, string field) => inner.CanReadField(type, field);

        public IReadOnlySet<string>? ReadLocales(SchemaType type) => inner.ReadLocales(type);

        public ContentFilter? RowRule(SchemaType type)
        {
            if (type != owner)
            {
                return inner.RowRule(type);
            }

            return _joined ??= inner.RowRule(type) is { } read ? new And(read, rule) : rule;
        }
    }

    // Compiles one rule on one type, for checking it against a single entry. The rule is trusted, so nothing else is asked.
    internal sealed class RuleOnly(SchemaType owner, ContentFilter rule) : IContentQueryAccess
    {
        public bool CanReadType(SchemaType type) => false;

        public bool CanReadDraft(SchemaType type) => false;

        public bool CanReadField(SchemaType type, string field) => false;

        public ContentFilter? RowRule(SchemaType type) => type == owner ? rule : Authorization.RowRule.NoRows;
    }
}
