using System.Globalization;
using System.Text.RegularExpressions;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// Checks the rules of section 4.2 of the backend specification and reports every violation, not only the first.
internal sealed class SchemaValidator(IFieldTypeRegistry fieldTypes)
{
    // Components and blocks may nest this many levels below a content type (decision 20).
    private const int MaxNestingDepth = 3;

    private const string BlockDiscriminator = "type";
    private const string BlockId = "id";

    // A decimal holds 28 digits.
    private const int MaxDecimalDigits = 28;

    // A select, component or blocks field without its setting is reported by the type-specific checks, with a message of its own.
    private static readonly HashSet<string> TypesThatReportMissingSettings = [FieldTypeNames.Select, FieldTypeNames.Component, FieldTypeNames.Blocks];

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
        ValidateTables(unique.Where(type => type.IsContentType));

        foreach (var type in unique.Where(type => type.IsContentType))
        {
            ValidateColumns(type, components);
        }

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
        else if (type.Kind == SchemaTypeKind.Block && string.Equals(field.Name, BlockId, StringComparison.OrdinalIgnoreCase))
        {
            Report(SchemaErrorCodes.ReservedName, $"field name '{field.Name}' of {location} is reserved for the block id");
        }

        var fieldType = fieldTypes.Find(field.FieldType);
        HashSet<string> wrongKind = [];
        if (fieldType is null)
        {
            Report(SchemaErrorCodes.FieldTypeNotRegistered, $"field type '{field.FieldType}' used by {location} is not registered");
        }
        else
        {
            wrongKind = ValidateSettingKeys(fieldType, field, location);
        }

        if (field.Localizable && !(type.IsContentType && type.Localized))
        {
            Report(SchemaErrorCodes.LocalizableOnUnlocalizedType, type.IsContentType
                ? $"field {location} is localizable, but {Describe(type)} is not localized"
                : $"field {location} is localizable, but fields of a {type.Kind.ToString().ToLowerInvariant()} cannot be; localize the field that uses it");
        }

        if (field.Multiple && fieldType is { SupportsMultiple: false })
        {
            Report(SchemaErrorCodes.InvalidField, $"{fieldType.Name} field {location} is marked multiple, but a {fieldType.Name} field holds a single value");
        }

        ValidateLimits(field, location);
        ValidateSettings(type, field, location, wrongKind, contentTypes, components, blocks);
        ValidateConditions(type, field, location);
    }

    // Returns the keys whose value has the wrong kind, so the type-specific checks do not report them a second time.
    private HashSet<string> ValidateSettingKeys(IFieldType fieldType, FieldDraft field, string location)
    {
        HashSet<string> wrongKind = [];

        foreach (var (key, value) in field.Settings.OrderBy(setting => setting.Key, StringComparer.Ordinal))
        {
            var descriptor = fieldType.Settings.FirstOrDefault(setting => setting.Key == key);
            if (descriptor is null)
            {
                if (!fieldType.AcceptsAnySetting)
                {
                    var accepted = fieldType.Settings.Count == 0 ? "it accepts none" : $"it accepts {string.Join(", ", fieldType.Settings.Select(setting => setting.Key))}";
                    Report(SchemaErrorCodes.InvalidField, $"{fieldType.Name} field {location} has an unknown setting '{key}'; {accepted}");
                }
            }
            else if (!HasKind(value, descriptor.Kind))
            {
                wrongKind.Add(key);
                Report(SchemaErrorCodes.InvalidField, $"setting '{key}' of {fieldType.Name} field {location} must be {Describe(descriptor.Kind)}");
            }
        }

        if (!TypesThatReportMissingSettings.Contains(fieldType.Name))
        {
            foreach (var descriptor in fieldType.Settings.Where(setting => setting.Required && !field.Settings.ContainsKey(setting.Key)))
            {
                Report(SchemaErrorCodes.InvalidField, $"{fieldType.Name} field {location} is missing the required setting '{descriptor.Key}'");
            }
        }

        return wrongKind;
    }

    private void ValidateDecimalBounds(FieldDraft field, string location)
    {
        long? precision = field.Settings.TryGetValue(FieldSettings.Precision, out var p) && p is long digits ? digits : null;
        long? scale = field.Settings.TryGetValue(FieldSettings.Scale, out var s) && s is long places ? places : null;

        var precisionFits = precision is null or (>= 1 and <= MaxDecimalDigits);
        var scaleFits = scale is null or (>= 0 and <= MaxDecimalDigits);

        if (!precisionFits)
        {
            Report(SchemaErrorCodes.InvalidField, $"decimal field {location} has a precision of {precision}; it must be between 1 and {MaxDecimalDigits}");
        }

        if (!scaleFits)
        {
            Report(SchemaErrorCodes.InvalidField, $"decimal field {location} has a scale of {scale}; it must be between 0 and {MaxDecimalDigits}");
        }

        if (precisionFits && scaleFits && scale > precision)
        {
            Report(SchemaErrorCodes.InvalidField, $"decimal field {location} has a scale of {scale} above its precision of {precision}");
        }
    }

    private static bool HasKind(object value, FieldSettingKind kind) => kind switch
    {
        FieldSettingKind.String => value is string,
        FieldSettingKind.Boolean => value is bool,
        FieldSettingKind.Integer => value is long,
        FieldSettingKind.Decimal => value is long or decimal,
        FieldSettingKind.StringList => value is IReadOnlyList<string>,
        _ => false,
    };

    private static string Describe(FieldSettingKind kind) => kind switch
    {
        FieldSettingKind.String => "a string",
        FieldSettingKind.Boolean => "true or false",
        FieldSettingKind.Integer => "a whole number",
        FieldSettingKind.Decimal => "a number",
        _ => "a list of strings",
    };

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

    private void ValidateSettings(TypeDraft type, FieldDraft field, string location, HashSet<string> wrongKind, Dictionary<string, TypeDraft> contentTypes, Dictionary<string, TypeDraft> components, Dictionary<string, TypeDraft> blocks)
    {
        if (field.Relation is not null && field.FieldType != FieldTypeNames.Relation)
        {
            Report(SchemaErrorCodes.InvalidField, $"field {location} has a relation target, but its type is '{field.FieldType}', not 'relation'");
        }

        if (!type.IsContentType && field.Relation?.Reverse is not null)
        {
            Report(SchemaErrorCodes.ReverseRelationOutsideContentType, $"relation {location} declares a reverse side, but reverse relations are only allowed on fields of collections and singletons, not of a {type.Kind.ToString().ToLowerInvariant()}");
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.Select when !wrongKind.Contains(FieldSettings.Options):
                var options = SettingList(field, FieldSettings.Options);
                if (options.Length == 0 || options.Distinct(StringComparer.Ordinal).Count() != options.Length)
                {
                    Report(SchemaErrorCodes.InvalidField, $"select field {location} needs at least one option, and no option twice");
                }

                break;

            case FieldTypeNames.Decimal:
                ValidateDecimalBounds(field, location);
                break;

            case FieldTypeNames.Slug:
                if (SettingText(field, FieldSettings.Source) is { } source
                    && (string.Equals(source, field.Name, StringComparison.Ordinal) || type.Fields.All(other => other.Name != source)))
                {
                    Report(SchemaErrorCodes.SlugSourceMissing, $"slug source '{source}' of {location} is not another field of {Describe(type)}");
                }

                break;

            case FieldTypeNames.Relation:
                if (field.Relation is null)
                {
                    Report(SchemaErrorCodes.InvalidField, $"relation field {location} has no target");
                }
                else if (!contentTypes.ContainsKey(field.Relation.Target))
                {
                    Report(SchemaErrorCodes.RelationTargetMissing, $"relation target '{field.Relation.Target}' of {location} is not a collection or singleton");
                }

                break;

            case FieldTypeNames.Component when !wrongKind.Contains(FieldSettings.Component):
                if (SettingText(field, FieldSettings.Component) is not { } component)
                {
                    Report(SchemaErrorCodes.InvalidField, $"component field {location} has no component");
                }
                else if (!components.ContainsKey(component))
                {
                    Report(SchemaErrorCodes.ComponentOrBlockMissing, $"component '{component}' used by {location} does not exist");
                }

                break;

            case FieldTypeNames.Blocks when !wrongKind.Contains(FieldSettings.Blocks):
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

    // A type's tables must not be a system table, nor the locale table of another type: 'page-i18n' beside a localized 'page'.
    private void ValidateTables(IEnumerable<TypeDraft> contentTypes)
    {
        var types = contentTypes.Where(type => SchemaNaming.IsValidTypeName(type.Name)).ToList();
        var localeTables = types.Where(type => type.Localized).ToDictionary(type => StorageNames.LocaleTable(type.Name), StringComparer.Ordinal);

        foreach (var type in types)
        {
            var table = StorageNames.Table(type.Name);
            if (StorageNames.SystemTables.Contains(table))
            {
                Report(SchemaErrorCodes.ReservedName, $"{Describe(type)} needs table {table}, which a system table already uses");
            }
            else if (localeTables.TryGetValue(table, out var owner))
            {
                Report(SchemaErrorCodes.ReservedName, $"{Describe(type)} needs table {table}, which is the locale table of {Describe(owner)}");
            }
        }
    }

    // Walks the fields that become columns, single components flattened, as the storage mapping does.
    private void ValidateColumns(TypeDraft type, Dictionary<string, TypeDraft> components)
    {
        Dictionary<(bool Localized, string Column), string> holders = [];

        foreach (var field in DistinctFields(type))
        {
            ValidateColumn(type, field, type.Name, "", type.Localized && field.Localizable, holders, components, []);
        }
    }

    // A repeated name is reported once, by ValidateType; the repeat would only add a second error about the same column.
    private static IEnumerable<FieldDraft> DistinctFields(TypeDraft type) =>
        type.Fields.DistinctBy(field => field.Name, StringComparer.OrdinalIgnoreCase);

    private void ValidateColumn(TypeDraft type, FieldDraft field, string owner, string prefix, bool localized, Dictionary<(bool Localized, string Column), string> holders, Dictionary<string, TypeDraft> components, HashSet<string> path)
    {
        var fieldType = fieldTypes.Find(field.FieldType);
        if (fieldType is null || !SchemaNaming.IsValidFieldName(field.Name))
        {
            return;
        }

        var location = $"{owner}.{field.Name}";
        var column = prefix + StorageNames.Column(field.Name);
        var kind = fieldType.StorageKind(SchemaModelFactory.Create(field));

        if (field.FieldType == FieldTypeNames.Component && kind == FieldStorageKind.Column)
        {
            if (SettingText(field, FieldSettings.Component) is { } name && components.TryGetValue(name, out var component) && path.Add(name))
            {
                foreach (var inner in DistinctFields(component))
                {
                    ValidateColumn(type, inner, location, column + "_", localized, holders, components, path);
                }

                path.Remove(name);
            }

            return;
        }

        if (field.Unique && kind != FieldStorageKind.Column)
        {
            Report(SchemaErrorCodes.InvalidField, kind == FieldStorageKind.Reference
                ? $"field {location} is unique, but a {field.FieldType} field has no column to index"
                : $"field {location} is unique, but a {field.FieldType} field is stored as JSON");
        }

        if (kind == FieldStorageKind.Reference)
        {
            return;
        }

        var table = localized ? StorageNames.LocaleTable(type.Name) : StorageNames.Table(type.Name);
        var systemColumns = localized ? StorageNames.LocaleTableColumns : StorageNames.SystemColumns;

        // A top-level field named like a system field was reported already, by name.
        var reportedByName = prefix.Length == 0 && ContentItem.ReservedFieldNames.Contains(field.Name);

        if (systemColumns.Contains(column))
        {
            if (!reportedByName)
            {
                Report(SchemaErrorCodes.ReservedName, $"field {location} maps to column {column} of {table}, which is a system column");
            }
        }
        else if (!holders.TryAdd((localized, column), location))
        {
            Report(SchemaErrorCodes.DuplicateField, $"fields {holders[(localized, column)]} and {location} both map to column {column} of {table}");
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
                if (field.FieldType == FieldTypeNames.Component
                    && SettingText(field, FieldSettings.Component) is { } name
                    && components.TryGetValue(name, out var component))
                {
                    yield return component;
                }
                else if (field.FieldType == FieldTypeNames.Blocks)
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
