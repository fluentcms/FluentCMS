using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FluentCMS.Analyzers;

// Reports, where a filter and sort builder of the source generator (B-95) is used, a field that has no index, so that a
// query on it reads every row of its type. The builder is matched to its class by the names the generator gives.
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class FilterIndexAnalyzer : DiagnosticAnalyzer
{
    private const string Content = "FluentCMS.Content.";

    // The builder types of a field that has a column of its own, which [Indexed] gives an index.
    private static readonly HashSet<string> ColumnFields = new(StringComparer.Ordinal)
    {
        Content + "TextField", Content + "NumberField`1", Content + "DateField`1", Content + "BooleanField", Content + "SelectField",
    };

    private static readonly HashSet<string> IndexableTypes = new(StringComparer.Ordinal)
    {
        "text", "slug", "email", "url", "integer", "decimal", "boolean", "date", "dateTime", "select",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rules.FilterNotIndexed];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(Content + "QueryField") is null)
            {
                return;
            }

            var builders = new ConcurrentDictionary<INamedTypeSymbol, Builder?>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(operation => Analyze(operation, builders), OperationKind.PropertyReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, ConcurrentDictionary<INamedTypeSymbol, Builder?> builders)
    {
        var property = ((IPropertyReferenceOperation)context.Operation).Property;
        if (property.Type is not INamedTypeSymbol fieldType
            || !ColumnFields.Contains(SchemaReader.FullName(fieldType.OriginalDefinition))
            || property.ContainingType is not { } builderType
            || !builderType.Name.EndsWith("Fields", StringComparison.Ordinal))
        {
            return;
        }

        if (builders.GetOrAdd(builderType, Find) is { } builder
            && builder.Fields.TryGetValue(property.Name, out var field)
            && field.FieldType is { } type && IndexableTypes.Contains(type) && !field.Multiple
            && field.Indexed is null && field.Unique is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.FilterNotIndexed, context.Operation.Syntax.GetLocation(), field.Name, builder.Schema.Label, builder.Schema.Name));
        }
    }

    // The class a builder was generated for: a collection or singleton has {Name}Fields, a component {Name}ComponentFields,
    // in the namespace of the class. A block's fields are JSON, which no index of a column reaches.
    private static Builder? Find(INamedTypeSymbol builderType)
    {
        foreach (var candidate in builderType.ContainingNamespace.GetTypeMembers())
        {
            if (SchemaReader.KindsOf(candidate).Count != 1 || SchemaReader.Read(candidate) is not { } schema)
            {
                continue;
            }

            var suffix = schema.Kind switch
            {
                SchemaKind.Component => "ComponentFields",
                SchemaKind.Block => null,
                _ => "Fields",
            };

            if (suffix is null || Pascal(schema.Name) + suffix != builderType.Name)
            {
                continue;
            }

            var fields = new Dictionary<string, SchemaField>(StringComparer.Ordinal);
            foreach (var field in SchemaReader.Fields(candidate, findings: null))
            {
                var member = Pascal(field.Name);
                if (!fields.ContainsKey(member))
                {
                    fields.Add(member, field);
                }
            }

            return new Builder(schema, fields);
        }

        return null;
    }

    // The class and member names of the emitter (FieldsEmitter.Pascal).
    private static string Pascal(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var part in name.Split('-'))
        {
            if (part.Length > 0)
            {
                builder.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
            }
        }

        return builder.ToString();
    }

    private sealed class Builder(SchemaClass schema, Dictionary<string, SchemaField> fields)
    {
        public SchemaClass Schema { get; } = schema;

        public Dictionary<string, SchemaField> Fields { get; } = fields;
    }
}
