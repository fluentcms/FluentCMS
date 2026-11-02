using System.Globalization;
using System.Text.RegularExpressions;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// Checks the rules of section 4.2 of the backend specification and reports every violation, not only the first.
internal sealed class SchemaValidator(IReadOnlySet<string> registeredFieldTypes)
{
    // Components and blocks may nest this many levels below a content type (decision 20).
    private const int MaxNestingDepth = 3;

    private const string BlockDiscriminator = "type";

    private readonly List<SchemaError> _errors = [];

    public IReadOnlyList<SchemaError> Validate(IReadOnlyList<TypeDraft> types)
    {
        _errors.Clear();

        var ordered = types.OrderBy(type => (int)type.Kind).ThenBy(type => type.Name, StringComparer.Ordinal).ToList();
        var contentTypes = new Dictionary<string, TypeDraft>(StringComparer.Ordinal);
        var components = new Dictionary<string, TypeDraft>(StringComparer.Ordinal);
        var blocks = new Dictionary<string, TypeDraft>(StringComparer.Ordinal);
        List<TypeDraft> unique = [];

        foreach (var type in ordered)
        {
            var group = type.IsContentType ? contentTypes : type.Kind == SchemaTypeKind.Component ? components : blocks;
            if (group.TryAdd(type.Name, type))
            {
                unique.Add(type);
            }
            else
            {
                Report(SchemaErrorCodes.DuplicateType, $"{Describe(type)} is defined more than once");
            }
        }

        foreach (var type in unique)
        {
            ValidateType(type, contentTypes, components, blocks);
        }

        ValidateReverseRelations(unique.Where(type => type.IsContentType), contentTypes);
        ValidateNesting(unique, components, blocks);

        return [.. _errors];
    }

    private static string Describe(TypeDraft type) => $"{type.Kind.ToString().ToLowerInvariant()} '{type.Name}'";

    private static Condition[] Leaves(Condition condition) =>
        condition.Operands.Count == 0 ? [condition] : [.. condition.Operands.SelectMany(Leaves)];

    private static string Limit(int? value) =>
        value is { } limit ? limit.ToString(CultureInfo.InvariantCulture) : "none";

    private static string[] SettingList(FieldDraft field, string key) =>
        field.Settings.TryGetValue(key, out var value) && value is IEnumerable<string> list ? [.. list] : [];

    private static string? SettingText(FieldDraft field, string key) =>
        field.Settings.TryGetValue(key, out var value) ? value as string : null;

    private void Report(string code, string message) => _errors.Add(new SchemaError(code, message));

    private void ValidateType(TypeDraft type, Dictionary<string, TypeDraft> contentTypes, Dictionary<string, TypeDraft> components, Dictionary<string, TypeDraft> blocks)
    {
        if (!SchemaNaming.IsValidTypeName(type.Name))
        {
            Report(SchemaErrorCodes.InvalidName, $"{Describe(type)} must be lower kebab-case: lower case letters and digits separated by single hyphens");
        }

        if (type.Localized && !type.IsContentType)
        {
            Report(SchemaErrorCodes.InvalidType, $"{Describe(type)} cannot be localized; only collections and singletons are");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var field in type.Fields)
        {
            if (!names.Add(field.Name))
            {
                Report(SchemaErrorCodes.DuplicateField, $"field '{field.Name}' is declared more than once in {Describe(type)}");
                continue;
            }

            ValidateField(type, field, contentTypes, components, blocks);
        }
    }

    private void ValidateField(TypeDraft type, FieldDraft field, Dictionary<string, TypeDraft> contentTypes, Dictionary<string, TypeDraft> components, Dictionary<string, TypeDraft> blocks)
    {
        var location = $"{type.Name}.{field.Name}";

        if (!SchemaNaming.IsValidFieldName(field.Name))
        {
            Report(SchemaErrorCodes.InvalidName, $"field name '{field.Name}' of {location} must be camelCase: a lower case letter followed by letters and digits");
        }
        else if (type.IsContentType && ContentItem.ReservedFieldNames.Contains(field.Name))
        {
            Report(SchemaErrorCodes.ReservedName, $"field name '{field.Name}' of {location} is reserved for a system field");
        }
        else if (type.Kind == SchemaTypeKind.Block && string.Equals(field.Name, BlockDiscriminator, StringComparison.OrdinalIgnoreCase))
        {
            Report(SchemaErrorCodes.ReservedName, $"field name '{field.Name}' of {location} is reserved for the block type discriminator");
        }

        if (!registeredFieldTypes.Contains(field.FieldType))
        {
            Report(SchemaErrorCodes.FieldTypeNotRegistered, $"field type '{field.FieldType}' used by {location} is not registered");
        }

        if (field.Localizable && !(type.IsContentType && type.Localized))
        {
            Report(SchemaErrorCodes.LocalizableOnUnlocalizedType, type.IsContentType
                ? $"field {location} is localizable, but {Describe(type)} is not localized"
                : $"field {location} is localizable, but fields of a {type.Kind.ToString().ToLowerInvariant()} cannot be; localize the field that uses it");
        }

        ValidateLimits(field, location);
        ValidateSettings(type, field, location, contentTypes, components, blocks);
        ValidateConditions(type, field, location);
    }

    private void ValidateLimits(FieldDraft field, string location)
    {
        if (field.MinLength < 0 || field.MaxLength < 1 || field.MinLength > field.MaxLength)
        {
            Report(SchemaErrorCodes.InvalidField, $"field {location} has an invalid length limit: minimum {Limit(field.MinLength)}, maximum {Limit(field.MaxLength)}");
        }

        if (field.Minimum > field.Maximum)
        {
            Report(SchemaErrorCodes.InvalidField, $"field {location} has a minimum value of {field.Minimum} above its maximum of {field.Maximum}");
        }

        if (field.Pattern is not null)
        {
            try
            {
                _ = new Regex(field.Pattern);
            }
            catch (ArgumentException exception)
            {
                Report(SchemaErrorCodes.InvalidField, $"field {location} has an invalid regular expression: {exception.Message}");
            }
        }
    }

    private void ValidateSettings(TypeDraft type, FieldDraft field, string location, Dictionary<string, TypeDraft> contentTypes, Dictionary<string, TypeDraft> components, Dictionary<string, TypeDraft> blocks)
    {
        if (field.Relation is not null && field.FieldType != KnownFieldTypes.Relation)
        {
            Report(SchemaErrorCodes.InvalidField, $"field {location} has a relation target, but its type is '{field.FieldType}', not 'relation'");
        }

        switch (field.FieldType)
        {
            case KnownFieldTypes.Select:
                var options = SettingList(field, FieldSettings.Options);
                if (options.Length == 0 || options.Distinct(StringComparer.Ordinal).Count() != options.Length)
                {
                    Report(SchemaErrorCodes.InvalidField, $"select field {location} needs at least one option, and no option twice");
                }

                break;

            case KnownFieldTypes.Slug:
                if (SettingText(field, FieldSettings.Source) is { } source
                    && (string.Equals(source, field.Name, StringComparison.Ordinal) || type.Fields.All(other => other.Name != source)))
                {
                    Report(SchemaErrorCodes.SlugSourceMissing, $"slug source '{source}' of {location} is not another field of {Describe(type)}");
                }

                break;

            case KnownFieldTypes.Relation:
                if (field.Relation is null)
                {
                    Report(SchemaErrorCodes.InvalidField, $"relation field {location} has no target");
                }
                else if (!contentTypes.ContainsKey(field.Relation.Target))
                {
                    Report(SchemaErrorCodes.RelationTargetMissing, $"relation target '{field.Relation.Target}' of {location} is not a collection or singleton");
                }

                break;

            case KnownFieldTypes.Component:
                if (SettingText(field, FieldSettings.Component) is not { } component)
                {
                    Report(SchemaErrorCodes.InvalidField, $"component field {location} has no component");
                }
                else if (!components.ContainsKey(component))
                {
                    Report(SchemaErrorCodes.ComponentOrBlockMissing, $"component '{component}' used by {location} does not exist");
                }

                break;

            case KnownFieldTypes.Blocks:
                var allowed = SettingList(field, FieldSettings.Blocks);
                if (allowed.Length == 0)
                {
                    Report(SchemaErrorCodes.InvalidField, $"blocks field {location} must list the blocks it accepts");
                }

                foreach (var block in allowed.Where(name => !blocks.ContainsKey(name)))
                {
                    Report(SchemaErrorCodes.ComponentOrBlockMissing, $"block '{block}' used by {location} does not exist");
                }

                break;
        }
    }

    private void ValidateConditions(TypeDraft type, FieldDraft field, string location)
    {
        foreach (var (kind, condition) in new[] { ("visibleWhen", field.VisibleWhen), ("requiredWhen", field.RequiredWhen) })
        {
            if (condition is null)
            {
                continue;
            }

            foreach (var name in Leaves(condition).Select(leaf => leaf.FieldName!).Distinct(StringComparer.Ordinal))
            {
                if (name == field.Name)
                {
                    Report(SchemaErrorCodes.InvalidCondition, $"{kind} of {location} refers to the field itself");
                }
                else if (type.Fields.All(other => other.Name != name))
                {
                    Report(SchemaErrorCodes.InvalidCondition, $"{kind} of {location} refers to '{name}', which is not a field of {Describe(type)}");
                }
            }
        }
    }

    private void ValidateReverseRelations(IEnumerable<TypeDraft> contentTypes, Dictionary<string, TypeDraft> targets)
    {
        Dictionary<(string Target, string Name), string> declared = [];

        foreach (var type in contentTypes)
        {
            foreach (var field in type.Fields.Where(field => field.Relation is { Reverse: not null } && targets.ContainsKey(field.Relation.Target)))
            {
                var relation = field.Relation!;
                var reverse = relation.Reverse!;
                var location = $"{type.Name}.{field.Name}";

                if (!SchemaNaming.IsValidFieldName(reverse))
                {
                    Report(SchemaErrorCodes.InvalidName, $"reverse relation name '{reverse}' of {location} must be camelCase: a lower case letter followed by letters and digits");
                }
                else if (ContentItem.ReservedFieldNames.Contains(reverse))
                {
                    Report(SchemaErrorCodes.ReservedName, $"reverse relation name '{reverse}' of {location} is reserved for a system field");
                }
                else if (targets[relation.Target].Fields.Any(other => string.Equals(other.Name, reverse, StringComparison.OrdinalIgnoreCase)))
                {
                    Report(SchemaErrorCodes.DuplicateReverseRelation, $"reverse relation name '{reverse}' of {location} is also a field of {relation.Target}");
                }
                else if (!declared.TryAdd((relation.Target, reverse.ToUpperInvariant()), location))
                {
                    Report(SchemaErrorCodes.DuplicateReverseRelation, $"reverse relation name '{reverse}' on {relation.Target} is declared by both {declared[(relation.Target, reverse.ToUpperInvariant())]} and {location}");
                }
            }
        }
    }

    // Depth counts the components and blocks between a content type and its deepest field. A cycle has no depth, so it is reported as such.
    private void ValidateNesting(IReadOnlyList<TypeDraft> types, Dictionary<string, TypeDraft> components, Dictionary<string, TypeDraft> blocks)
    {
        Dictionary<TypeDraft, int> depths = [];
        Dictionary<TypeDraft, TypeDraft> deepest = [];
        HashSet<string> cycles = [];

        IEnumerable<TypeDraft> Children(TypeDraft type)
        {
            foreach (var field in type.Fields)
            {
                if (field.FieldType == KnownFieldTypes.Component
                    && SettingText(field, FieldSettings.Component) is { } name
                    && components.TryGetValue(name, out var component))
                {
                    yield return component;
                }
                else if (field.FieldType == KnownFieldTypes.Blocks)
                {
                    foreach (var block in SettingList(field, FieldSettings.Blocks))
                    {
                        if (blocks.TryGetValue(block, out var child))
                        {
                            yield return child;
                        }
                    }
                }
            }
        }

        int Visit(TypeDraft type, List<TypeDraft> path)
        {
            if (depths.TryGetValue(type, out var known))
            {
                return known;
            }

            var index = path.IndexOf(type);
            if (index >= 0)
            {
                var cycle = path.Skip(index).Select(member => member.Name).ToList();
                if (cycles.Add(string.Join('>', cycle.Order(StringComparer.Ordinal))))
                {
                    Report(SchemaErrorCodes.NestingTooDeep, $"{string.Join(" > ", cycle.Append(type.Name))} nests {Describe(type)} inside itself, so its depth is unbounded; the limit is {MaxNestingDepth}");
                }

                return 0;
            }

            path.Add(type);
            var deepestChild = 0;
            foreach (var child in Children(type).Distinct().ToList())
            {
                var childDepth = Visit(child, path);
                if (childDepth > deepestChild)
                {
                    deepestChild = childDepth;
                    deepest[type] = child;
                }
            }

            path.RemoveAt(path.Count - 1);

            var depth = deepestChild + 1;
            depths[type] = depth;

            if (depth > MaxNestingDepth && deepestChild <= MaxNestingDepth)
            {
                List<string> chain = [];
                for (TypeDraft? current = type; current is not null && chain.Count < depth; deepest.TryGetValue(current, out current))
                {
                    chain.Add(current.Name);
                }

                Report(SchemaErrorCodes.NestingTooDeep, $"{Describe(type)} nests {depth} levels of components and blocks ({string.Join(" > ", chain)}); the limit is {MaxNestingDepth}");
            }

            return depth;
        }

        foreach (var type in types.Where(type => !type.IsContentType))
        {
            Visit(type, []);
        }
    }
}
