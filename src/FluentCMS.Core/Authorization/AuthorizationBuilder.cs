using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// cms.Authorization: registers row rules, which roles then name as data.
public sealed class AuthorizationBuilder
{
    private readonly List<RowRule> _rules = [];

    internal AuthorizationBuilder()
    {
        // The seeded Author role names this one with content.*, so it holds for every type without a registration.
        AddRowRule<ContentItem>(BuiltInRoles.OwnEntriesRule, ctx => item => item.CreatedBy == ctx.Principal.Id);
    }

    internal IReadOnlyList<RowRule> Rules => _rules;

    // T names the content type by its [Collection] or [Singleton] name, and ContentItem means every type. The class
    // only has to describe the fields the rule reads, so a type defined in a schema file can have a small one for this.
    // A predicate is translated to a filter, not run; RowRuleTranslator lists what it accepts, and refuses the rest here.
    public AuthorizationBuilder AddRowRule<T>(string name, Expression<Func<OperationContext, Expression<Func<T, bool>>>> rule)
        where T : ContentItem
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rule);

        var typeName = typeof(T) == typeof(ContentItem) ? null : AttributeSchemaReader.NameOf(typeof(T));
        _rules.Add(RowRuleTranslator.Translate(name, typeName, rule));
        return this;
    }
}
