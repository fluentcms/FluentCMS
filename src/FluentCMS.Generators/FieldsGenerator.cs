using System.Collections.Immutable;
using System.Text;
using FluentCMS.Generators.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FluentCMS.Generators;

// Emits the typed filter and sort builders, such as ArticleFields.Title.Contains("x"), for the content types,
// components and blocks the compilation declares with the schema attributes of FluentCMS.
[Generator(LanguageNames.CSharp)]
public sealed class FieldsGenerator : IIncrementalGenerator
{
    internal const string TypesStep = "FluentCMS.Types";
    internal const string EmitStep = "FluentCMS.Emit";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var collections = Types(context, SymbolReader.CollectionAttribute);
        var singletons = Types(context, SymbolReader.SingletonAttribute);
        var components = Types(context, SymbolReader.ComponentAttribute);
        var blocks = Types(context, SymbolReader.BlockAttribute);

        var types = collections.Combine(singletons).Combine(components).Combine(blocks)
            .Select(static (all, _) => Merge(all.Left.Left.Left, all.Left.Left.Right, all.Left.Right, all.Right))
            .WithTrackingName(TypesStep);

        var output = types
            .Select(static (types, _) => new GeneratorOutput(types, FieldsEmitter.Emit(types.Select(type => type.Description), EmitOptions.Sdk)))
            .WithTrackingName(EmitStep);

        context.RegisterSourceOutput(output, static (production, output) => Produce(production, output));
    }

    private static IncrementalValueProvider<ImmutableArray<TypeModel>> Types(IncrementalGeneratorInitializationContext context, string attribute) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attribute,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (syntax, cancellationToken) => SymbolReader.Read(syntax, cancellationToken))
            .Where(static type => type is not null)
            .Select(static (type, _) => type!)
            .Collect();

    private static EquatableArray<TypeModel> Merge(params ImmutableArray<TypeModel>[] groups) =>
        groups.SelectMany(group => group)
            .OrderBy(type => type.Description.Kind)
            .ThenBy(type => type.Description.Name, StringComparer.Ordinal)
            .ThenBy(type => type.Description.Namespace, StringComparer.Ordinal)
            .ToEquatableArray();

    private static void Produce(SourceProductionContext production, GeneratorOutput output)
    {
        foreach (var source in output.Result.Sources)
        {
            production.AddSource(source.HintName, SourceText.From(source.Text, Encoding.UTF8));
        }

        foreach (var type in output.Types)
        {
            foreach (var diagnostic in type.Diagnostics)
            {
                production.ReportDiagnostic(Diagnostic.Create(Descriptors.For(diagnostic.Code), diagnostic.Location?.ToLocation() ?? Location.None, diagnostic.Message));
            }
        }

        foreach (var diagnostic in output.Result.Diagnostics)
        {
            production.ReportDiagnostic(Diagnostic.Create(Descriptors.For(diagnostic.Code), LocationOf(output.Types, diagnostic) ?? Location.None, diagnostic.Message));
        }
    }

    private static Location? LocationOf(EquatableArray<TypeModel> types, EmitDiagnostic diagnostic)
    {
        var type = types.FirstOrDefault(type => type.Description.Kind == diagnostic.Kind && type.Description.Name == diagnostic.TypeName && type.Description.Namespace == diagnostic.Namespace);
        if (type is null)
        {
            return null;
        }

        var field = diagnostic.FieldName is null ? null : type.FieldLocations.FirstOrDefault(location => location.Field == diagnostic.FieldName);
        return (field?.Location ?? type.Location)?.ToLocation();
    }
}
