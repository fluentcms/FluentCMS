using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Agents;

// What a credential may touch (MCP-05). Sites are always listed, so a site added later is not reachable by default. Types and
// fields are either a list that narrows or null for all of them; an empty list is refused rather than read as "none".
public sealed class AgentScope
{
    private AgentScope(
        IReadOnlyList<Guid> sites,
        IReadOnlyList<string>? types,
        IReadOnlyList<string>? fields,
        IReadOnlyList<AgentOperation> operations)
    {
        Sites = sites;
        Types = types;
        Fields = fields;
        Operations = operations;
    }

    public IReadOnlyList<Guid> Sites { get; }

    // Content type slugs; null for every type of the sites in scope.
    public IReadOnlyList<string>? Types { get; }

    // Names such as "product.price". A type named here is narrowed to the fields listed for it, and the other types in scope
    // keep all their fields; null for every field.
    public IReadOnlyList<string>? Fields { get; }

    public IReadOnlyList<AgentOperation> Operations { get; }

    public static Result<AgentScope> Create(
        IEnumerable<Guid> sites,
        IEnumerable<string>? types,
        IEnumerable<string>? fields,
        IEnumerable<AgentOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(operations);

        var errors = new List<Error>();
        var siteList = sites.Distinct().ToList();
        var typeList = types?.Distinct(StringComparer.Ordinal).ToList();
        var fieldList = fields?.Distinct(StringComparer.Ordinal).ToList();
        var operationList = operations.Distinct().ToList();

        if (siteList.Count == 0)
        {
            errors.Add(new Error(AgentCredentialErrorCodes.ScopeSitesMissing, "A credential names the sites it may touch, and none was given."));
        }
        else if (siteList.Contains(Guid.Empty))
        {
            errors.Add(new Error(AgentCredentialErrorCodes.ScopeSiteInvalid, "A site id in the scope is empty."));
        }

        if (typeList is { Count: 0 })
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.ScopeTypesEmpty,
                "The list of content types is empty; name at least one, or leave the list out to allow every type."));
        }

        errors.AddRange(typeList?.Where(type => !SchemaNames.IsSlug(type)).Select(type => new Error(
            AgentCredentialErrorCodes.ScopeTypeInvalid,
            $"'{type}' is not a content type slug; use lowercase words joined by hyphens, such as 'product-page'.")) ?? []);

        if (fieldList is { Count: 0 })
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.ScopeFieldsEmpty,
                "The list of fields is empty; name at least one, or leave the list out to allow every field."));
        }

        foreach (var field in fieldList ?? [])
        {
            if (Split(field) is not { } name || !SchemaNames.IsSlug(name.Type) || !SchemaNames.IsFieldName(name.Field))
            {
                errors.Add(new Error(
                    AgentCredentialErrorCodes.ScopeFieldInvalid,
                    $"'{field}' is not a field name; write the content type slug and the field joined by a dot, such as 'product.price'."));
            }
            else if (typeList is not null && !typeList.Contains(name.Type, StringComparer.Ordinal))
            {
                errors.Add(new Error(
                    AgentCredentialErrorCodes.ScopeFieldOutsideTypes,
                    $"The field '{field}' belongs to '{name.Type}', which is not one of the content types in the scope; add the type or remove the field."));
            }
        }

        if (operationList.Count == 0)
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.ScopeOperationsMissing,
                "A credential names the operations it may perform: read, create, update or delete."));
        }

        errors.AddRange(operationList.Where(operation => !Enum.IsDefined(operation)).Select(operation => new Error(
            AgentCredentialErrorCodes.ScopeOperationInvalid,
            $"The operation '{operation}' is not one of read, create, update or delete.")));

        return errors.Count == 0
            ? Result<AgentScope>.Success(new AgentScope(siteList, typeList, fieldList, operationList))
            : Result<AgentScope>.Failure(errors);
    }

    // Rebuilds a stored scope without checking it again, so it stays readable if the rules above change.
    public static AgentScope Restore(
        IEnumerable<Guid> sites,
        IEnumerable<string>? types,
        IEnumerable<string>? fields,
        IEnumerable<AgentOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(operations);

        return new AgentScope([.. sites], types?.ToList(), fields?.ToList(), [.. operations]);
    }

    public bool AllowsSite(Guid siteId)
    {
        return Sites.Contains(siteId);
    }

    public bool AllowsType(string type)
    {
        return Types is null || Types.Contains(type, StringComparer.Ordinal);
    }

    public bool AllowsField(string type, string field)
    {
        if (!AllowsType(type))
        {
            return false;
        }

        var listed = (Fields ?? []).Select(Split).Where(name => name?.Type == type).ToList();
        return listed.Count == 0 || listed.Any(name => name?.Field == field);
    }

    public bool AllowsOperation(AgentOperation operation)
    {
        return Operations.Contains(operation);
    }

    private static (string Type, string Field)? Split(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? null : (name[..dot], name[(dot + 1)..]);
    }
}
