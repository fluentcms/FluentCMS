using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FluentCMS.Analyzers;

// Reports, at the attribute or member that causes it, what schema validation would refuse when the host starts (spec 4.2
// and 6.4). It checks one class at a time; what needs the whole schema, such as a duplicate type name or the nesting
// depth, is left to the host.
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class SchemaAttributeAnalyzer : DiagnosticAnalyzer
{
    // The field types that hold a single value, so that a list of them is refused.
    private static readonly HashSet<string> SingleValueTypes = new(StringComparer.Ordinal)
    {
        "text", "longText", "richText", "integer", "boolean", "date", "dateTime", "email", "url", "slug",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Rules.KindsCombined, Rules.WrongBase, Rules.PublishPolicyNotPublished, Rules.NoFieldType, Rules.FieldTypesCombined,
        Rules.NotPublic, Rules.EnumWithOptions, Rules.NoOptions, Rules.NotAReference, Rules.NotAComponent, Rules.NoBlockTypes,
        Rules.NotABlock, Rules.TwoMaximumLengths, Rules.ListOfSingleValue, Rules.UniqueWithoutColumn, Rules.ReverseOutsideContentType,
        Rules.ReservedFieldName, Rules.ReservedTypeName, Rules.TypeNameTooLong, Rules.FieldNameTooLong, Rules.SlugSourceMissing,
        Rules.RelationTargetNotContentType, Rules.LocalizableOnUnlocalizedType,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // A property of a base class is read again for every class that derives from it, but reported once.
            var reported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            start.RegisterSymbolAction(symbol => Analyze(symbol, reported), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> reported)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || SchemaReader.IsGeneric(type))
        {
            return;
        }

        var kinds = SchemaReader.KindsOf(type);
        if (kinds.Count == 0)
        {
            return;
        }

        var anchor = type.Locations.FirstOrDefault(location => location.IsInSource);
        void Report(DiagnosticDescriptor rule, Location? location, params object[] arguments)
        {
            location = location is { IsInSource: true } && context.Compilation.ContainsSyntaxTree(location.SourceTree) ? location : anchor;
            var diagnostic = Diagnostic.Create(rule, location, arguments);
            if (reported.TryAdd($"{rule.Id}|{location?.SourceTree?.FilePath}|{location?.SourceSpan}|{diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}", 0))
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        if (kinds.Count > 1)
        {
            Report(Rules.KindsCombined, anchor, type.Name);
            return;
        }

        var schema = SchemaReader.Discover(type)!;
        if (schema.IsContentType != SchemaReader.DerivesFrom(type, SchemaReader.ContentItem))
        {
            Report(Rules.WrongBase, anchor, schema.Label, type.Name, schema.IsContentType ? "must" : "must not");
            return;
        }

        if (!schema.IsContentType && type.GetAttributes().FirstOrDefault(a => SchemaReader.FullName(a.AttributeClass) == "FluentCMS.Publishing.PublishPolicyAttribute") is { } policy)
        {
            Report(Rules.PublishPolicyNotPublished, SchemaReader.LocationOf(policy), schema.Label, type.Name);
        }

        List<Finding> findings = [];
        var fields = SchemaReader.Fields(type, findings);
        foreach (var finding in findings)
        {
            Report(finding.Rule, finding.Location, finding.Arguments);
        }

        ValidateType(schema, Report);
        ValidateFields(schema, fields, Report);
        if (schema.IsContentType)
        {
            ValidateColumns(schema, fields, Report);
        }
    }

    private delegate void Reporter(DiagnosticDescriptor rule, Location? location, params object[] arguments);

    private static Location? NameLocation(SchemaClass schema) =>
        schema.Attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax { ArgumentList.Arguments: { Count: > 0 } arguments } && arguments[0].NameEquals is null
            ? arguments[0].GetLocation()
            : schema.Symbol.Locations.FirstOrDefault();

    private static void ValidateType(SchemaClass schema, Reporter report)
    {
        if (schema.Name.Length > StorageNames.NameLength)
        {
            report(Rules.TypeNameTooLong, NameLocation(schema), schema.Label, schema.Name, $"has a name of {schema.Name.Length} characters, and a type name has at most {StorageNames.NameLength}");
        }

        if (!schema.IsContentType || !StorageNames.IsValidTypeName(schema.Name))
        {
            return;
        }

        var table = StorageNames.Table(schema.Name);
        if (StorageNames.SystemTables.Contains(table))
        {
            report(Rules.ReservedTypeName, NameLocation(schema), schema.Label, schema.Name, table);
        }

        var longest = schema.Localized ? StorageNames.LocaleTable(schema.Name) : table;
        if (schema.Name.Length <= StorageNames.NameLength && longest.Length > StorageNames.MaxIdentifierLength)
        {
            report(Rules.TypeNameTooLong, NameLocation(schema), schema.Label, schema.Name, $"needs the table {longest} of {longest.Length} characters, and a table name has at most {StorageNames.MaxIdentifierLength}");
        }
    }

    // Mirrors ValidateType and ValidateField of the schema validator: a name repeated without regard to case is
    // reported by the host as a duplicate and checked no further.
    private static void ValidateFields(SchemaClass schema, List<SchemaField> fields, Reporter report)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!names.Add(field.Name))
            {
                continue;
            }

            var location = $"{schema.Name}.{field.Name}";
            var identifier = field.Property.Locations.FirstOrDefault();

            if (StorageNames.IsValidFieldName(field.Name))
            {
                if (schema.IsContentType && StorageNames.ReservedFieldNames.Contains(field.Name))
                {
                    report(Rules.ReservedFieldName, identifier, location, "has a name reserved for a system field");
                }
                else if (schema.Kind == SchemaKind.Block && string.Equals(field.Name, "type", StringComparison.OrdinalIgnoreCase))
                {
                    report(Rules.ReservedFieldName, identifier, location, "has a name reserved for the block type discriminator");
                }
                else if (schema.Kind == SchemaKind.Block && string.Equals(field.Name, "id", StringComparison.OrdinalIgnoreCase))
                {
                    report(Rules.ReservedFieldName, identifier, location, "has a name reserved for the block id");
                }
            }

            if (field.Name.Length > StorageNames.NameLength)
            {
                report(Rules.FieldNameTooLong, identifier, location, $"has a name of {field.Name.Length} characters, and a field name has at most {StorageNames.NameLength}");
            }

            if (field.Localizable is { } localizable && !(schema.IsContentType && schema.Localized))
            {
                report(Rules.LocalizableOnUnlocalizedType, SchemaReader.LocationOf(localizable), location, schema.IsContentType
                    ? $"the {schema.Label} '{schema.Name}' is not localized"
                    : $"fields of a {schema.Label} cannot be; localize the field that uses it");
            }

            if (field.Multiple && field.FieldType is { } fieldType && SingleValueTypes.Contains(fieldType))
            {
                report(Rules.ListOfSingleValue, SchemaReader.LocationOf(field.FieldAttribute), fieldType, location);
            }

            if (field.SlugSource is { } declared)
            {
                var source = Generators.SchemaNames.ToCamelCase(declared);
                if (source == field.Name || fields.All(other => other.Name != source))
                {
                    report(Rules.SlugSourceMissing, SchemaReader.ArgumentLocation(field.FieldAttribute, "Source"), source, location, schema.Name);
                }
            }

            if (field.IsRelation && field.Reverse is { } reverse)
            {
                if (!schema.IsContentType)
                {
                    report(Rules.ReverseOutsideContentType, SchemaReader.ArgumentLocation(field.FieldAttribute, "Reverse"), location);
                }
                else if (field.TargetKnown && StorageNames.IsValidFieldName(reverse) && StorageNames.ReservedFieldNames.Contains(reverse))
                {
                    report(Rules.ReservedFieldName, SchemaReader.ArgumentLocation(field.FieldAttribute, "Reverse"), location, $"declares the reverse relation '{reverse}', a name reserved for a system field");
                }
            }
        }
    }

    // Mirrors ValidateColumns of the schema validator: the fields that become columns, single components flattened.
    private static void ValidateColumns(SchemaClass schema, List<SchemaField> fields, Reporter report)
    {
        foreach (var field in Distinct(fields))
        {
            var anchor = field.Property.Locations.FirstOrDefault();
            ValidateColumn(schema, field, schema.Name, "", schema.Localized && field.Localizable is not null, report, anchor, [], lengthReported: false);
        }
    }

    private static IEnumerable<SchemaField> Distinct(List<SchemaField> fields)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return fields.Where(field => names.Add(field.Name));
    }

    private static void ValidateColumn(SchemaClass schema, SchemaField field, string owner, string prefix, bool localized, Reporter report, Location? anchor, HashSet<ISymbol> path, bool lengthReported)
    {
        // A field type of a plugin, media included, is checked only when the host knows the plugin is there.
        if (field.FieldType is not { } fieldType || fieldType == "media" || !StorageNames.IsValidFieldName(field.Name))
        {
            return;
        }

        // Inside a component the place is the inner property when it is in source, and otherwise the field that holds it.
        Location? Prefer(Location? location) => location is { IsInSource: true } ? location : anchor;
        var here = prefix.Length == 0 ? anchor : Prefer(field.Property.Locations.FirstOrDefault());
        var location = $"{owner}.{field.Name}";
        var column = prefix + StorageNames.Column(field.Name);
        var kind = StorageKind(field);
        var reportedLength = lengthReported || field.Name.Length > StorageNames.NameLength;

        if (fieldType == "component" && kind == Storage.Column)
        {
            if (field.Component is { Kind: SchemaKind.Component } component && path.Add(component.Symbol))
            {
                foreach (var inner in Distinct(SchemaReader.Fields(component.Symbol, findings: null)))
                {
                    ValidateColumn(schema, inner, location, column + "_", localized, report, anchor, path, reportedLength);
                }

                path.Remove(component.Symbol);
            }

            return;
        }

        if (field.Unique is { } unique && kind != Storage.Column)
        {
            report(Rules.UniqueWithoutColumn, Prefer(SchemaReader.LocationOf(unique)), location, fieldType, kind == Storage.Reference ? "has no column to index" : "is stored as JSON");
        }

        if (kind == Storage.Reference)
        {
            var apiPath = location.Substring(schema.Name.Length + 1);
            if (!reportedLength && apiPath.Length > StorageNames.NameLength)
            {
                report(Rules.FieldNameTooLong, here, location, $"is stored by its path {apiPath} of {apiPath.Length} characters, and a path has at most {StorageNames.NameLength}");
            }

            return;
        }

        var table = localized ? StorageNames.LocaleTable(schema.Name) : StorageNames.Table(schema.Name);
        if (!reportedLength && column.Length > StorageNames.MaxIdentifierLength)
        {
            report(Rules.FieldNameTooLong, here, location, $"maps to the column {column} of {table}, which has {column.Length} characters, and a column name has at most {StorageNames.MaxIdentifierLength}");
        }

        var reportedByName = prefix.Length == 0 && StorageNames.ReservedFieldNames.Contains(field.Name);
        var systemColumns = localized ? StorageNames.LocaleTableColumns : StorageNames.SystemColumns;
        if (systemColumns.Contains(column) && !reportedByName)
        {
            report(Rules.ReservedFieldName, here, location, $"maps to the column {column} of {table}, which is a system column");
        }
    }

    private enum Storage
    {
        Column,
        Json,
        Reference,
    }

    // The storage kinds of the core field types (spec 4.2), with their Multiple.
    private static Storage StorageKind(SchemaField field) => field.FieldType switch
    {
        "relation" => Storage.Reference,
        "json" or "richText" or "blocks" => Storage.Json,
        "select" or "component" => field.Multiple ? Storage.Json : Storage.Column,
        _ => Storage.Column,
    };
}
