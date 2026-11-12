using FluentCMS.Generators.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluentCMS.Generators;

// Reads a schema class the way the attribute reader of FluentCMS.Core does (spec 4.2), from symbols instead of
// reflection. What the reader refuses is left out without a word: the host reports it at start and the analyzers in
// the editor, so the generator only says why a field it could read gets no builder.
internal static class SymbolReader
{
    public const string CollectionAttribute = "FluentCMS.Schema.CollectionAttribute";
    public const string SingletonAttribute = "FluentCMS.Schema.SingletonAttribute";
    public const string ComponentAttribute = "FluentCMS.Schema.ComponentAttribute";
    public const string BlockAttribute = "FluentCMS.Schema.BlockAttribute";

    private const string FieldAttribute = "FluentCMS.Schema.FieldAttribute";
    private const string ContentItem = "FluentCMS.Schema.ContentItem";

    private static readonly Dictionary<string, string> FieldTypes = new(StringComparer.Ordinal)
    {
        ["FluentCMS.Schema.TextAttribute"] = "text",
        ["FluentCMS.Schema.LongTextAttribute"] = "longText",
        ["FluentCMS.Schema.RichTextAttribute"] = "richText",
        ["FluentCMS.Schema.IntegerAttribute"] = "integer",
        ["FluentCMS.Schema.DecimalAttribute"] = "decimal",
        ["FluentCMS.Schema.BooleanAttribute"] = "boolean",
        ["FluentCMS.Schema.DateAttribute"] = "date",
        ["FluentCMS.Schema.DateTimeAttribute"] = "dateTime",
        ["FluentCMS.Schema.EmailAttribute"] = "email",
        ["FluentCMS.Schema.UrlAttribute"] = "url",
        ["FluentCMS.Schema.SlugAttribute"] = "slug",
        ["FluentCMS.Schema.SelectAttribute"] = "select",
        ["FluentCMS.Schema.JsonAttribute"] = "json",
        ["FluentCMS.Schema.RelationAttribute"] = "relation",
        ["FluentCMS.Schema.ComponentAttribute"] = "component",
        ["FluentCMS.Schema.BlocksAttribute"] = "blocks",
        ["FluentCMS.Schema.MediaAttribute"] = "media",
    };

    private static readonly HashSet<string> CollectionTypes = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.IEnumerable`1",
    };

    public static TypeModel? Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || KindOf(type) is not { } kind)
        {
            return null;
        }

        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsGenericType)
        {
            return null;
        }

        var isContentType = kind.Kind is BuilderKind.Collection or BuilderKind.Singleton;
        if (isContentType != DerivesFrom(type, ContentItem))
        {
            return null;
        }

        List<FieldDescription> fields = [];
        List<FieldLocation> locations = [];
        List<DiagnosticInfo> diagnostics = [];
        foreach (var property in Properties(type))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadField(property, diagnostics) is { } field)
            {
                fields.Add(field);
                if (LocationInfo.From(property.Locations.FirstOrDefault()) is { } location)
                {
                    locations.Add(new FieldLocation(field.Name, location));
                }
            }
        }

        var @namespace = type.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() : "";
        var name = kind.Name ?? SchemaNames.ToKebabCase(type.Name);
        var typeLocation = context.TargetNode is TypeDeclarationSyntax declaration ? declaration.Identifier.GetLocation() : type.Locations.FirstOrDefault();

        return new TypeModel(
            new TypeDescription(name, kind.Kind, @namespace, fields.ToEquatableArray()),
            LocationInfo.From(typeLocation),
            locations.ToEquatableArray(),
            diagnostics.ToEquatableArray());
    }

    private static (BuilderKind Kind, string? Name)? KindOf(INamedTypeSymbol type)
    {
        List<(BuilderKind, string?)> kinds = [];
        foreach (var attribute in type.GetAttributes())
        {
            BuilderKind? kind = FullName(attribute.AttributeClass) switch
            {
                CollectionAttribute => BuilderKind.Collection,
                SingletonAttribute => BuilderKind.Singleton,
                ComponentAttribute => BuilderKind.Component,
                BlockAttribute => BuilderKind.Block,
                _ => null,
            };

            if (kind is { } found)
            {
                var name = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
                kinds.Add((found, name));
            }
        }

        return kinds.Count == 1 ? kinds[0] : null;
    }

    private static string NameOf(INamedTypeSymbol type) => KindOf(type)?.Name ?? SchemaNames.ToKebabCase(type.Name);

    // The properties of the class and its bases below ContentItem, the base's first, each in declaration order.
    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type)
    {
        List<INamedTypeSymbol> chain = [];
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object && FullName(current) != ContentItem; current = current.BaseType)
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain.SelectMany(declaring => declaring.GetMembers().OfType<IPropertySymbol>())
            .Where(property => !property.IsStatic && !property.IsIndexer && !property.IsOverride);
    }

    private static FieldDescription? ReadField(IPropertySymbol property, List<DiagnosticInfo> diagnostics)
    {
        var fieldAttributes = property.GetAttributes().Where(attribute => DerivesFrom(attribute.AttributeClass, FieldAttribute)).ToList();
        if (fieldAttributes.Count != 1
            || property.GetMethod is not { DeclaredAccessibility: Accessibility.Public }
            || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
        {
            return null;
        }

        var attribute = fieldAttributes[0];
        var name = SchemaNames.ToCamelCase(property.Name);
        if (!FieldTypes.TryGetValue(FullName(attribute.AttributeClass), out var fieldType))
        {
            diagnostics.Add(new DiagnosticInfo(
                EmitCodes.UnknownFieldType,
                $"The field '{name}' of {property.ContainingType.Name} gets no builder, because the generator does not know the field type of [{attribute.AttributeClass?.Name}].",
                LocationInfo.From(property.Locations.FirstOrDefault())));
            return null;
        }

        var (element, multiple) = Unwrap(property.Type);
        switch (fieldType)
        {
            case "relation":
                if (NotNullable(property.Type) is not INamedTypeSymbol { IsGenericType: true } reference
                    || FullName(reference.ConstructedFrom) is not ("FluentCMS.Schema.Ref`1" or "FluentCMS.Schema.RefList`1")
                    || reference.TypeArguments[0] is not INamedTypeSymbol target
                    || KindOf(target)?.Kind is not (BuilderKind.Collection or BuilderKind.Singleton))
                {
                    return null;
                }

                return Field(name, fieldType, FullName(reference.ConstructedFrom) == "FluentCMS.Schema.RefList`1");

            case "component":
                return element is INamedTypeSymbol { TypeKind: TypeKind.Class } component && KindOf(component)?.Kind == BuilderKind.Component
                    ? new FieldDescription(name, fieldType, multiple, NameOf(component), EquatableArray<string>.Empty)
                    : null;

            case "blocks":
                List<string> blocks = [];
                foreach (var argument in attribute.ConstructorArguments.Where(argument => argument.Kind == TypedConstantKind.Array).SelectMany(argument => argument.Values))
                {
                    if (argument.Value is not INamedTypeSymbol block || KindOf(block)?.Kind != BuilderKind.Block)
                    {
                        return null;
                    }

                    blocks.Add(NameOf(block));
                }

                return blocks.Count == 0 ? null : new FieldDescription(name, fieldType, false, null, blocks.ToEquatableArray());

            case "media":
                return Field(name, fieldType, FullName(property.Type as INamedTypeSymbol) == "FluentCMS.Media.MediaRefList");

            case "json":
                return Field(name, fieldType, false);

            default:
                return Field(name, fieldType, multiple);
        }
    }

    private static FieldDescription Field(string name, string fieldType, bool multiple) =>
        new(name, fieldType, multiple, null, EquatableArray<string>.Empty);

    private static (ITypeSymbol Element, bool Multiple) Unwrap(ITypeSymbol type)
    {
        type = NotNullable(type);
        if (type is IArrayTypeSymbol array)
        {
            return (NotNullable(array.ElementType), true);
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic && CollectionTypes.Contains(FullName(generic.ConstructedFrom)))
        {
            return (NotNullable(generic.TypeArguments[0]), true);
        }

        return (type, false);
    }

    private static ITypeSymbol NotNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    private static bool DerivesFrom(INamedTypeSymbol? type, string baseName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (FullName(current) == baseName)
            {
                return true;
            }
        }

        return false;
    }

    private static string FullName(INamedTypeSymbol? type) =>
        type is null ? "" : type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;
}
