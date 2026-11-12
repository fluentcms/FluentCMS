using System.Collections.Immutable;
using FluentCMS.Generators;
using Microsoft.CodeAnalysis;

namespace FluentCMS.Analyzers;

internal enum SchemaKind
{
    Collection,
    Singleton,
    Component,
    Block,
}

internal sealed class SchemaClass(INamedTypeSymbol symbol, SchemaKind kind, string name, bool localized, AttributeData attribute)
{
    public INamedTypeSymbol Symbol { get; } = symbol;

    public SchemaKind Kind { get; } = kind;

    public string Name { get; } = name;

    public bool Localized { get; } = localized;

    public AttributeData Attribute { get; } = attribute;

    public bool IsContentType => Kind is SchemaKind.Collection or SchemaKind.Singleton;

    public string Label => Kind.ToString().ToLowerInvariant();
}

internal sealed class SchemaField(IPropertySymbol property, string name, string? fieldType, AttributeData fieldAttribute)
{
    public IPropertySymbol Property { get; } = property;

    public string Name { get; } = name;

    // Null for a field type the analyzers do not know, such as a plugin's.
    public string? FieldType { get; } = fieldType;

    public AttributeData FieldAttribute { get; } = fieldAttribute;

    public bool Multiple { get; set; }

    public AttributeData? Localizable { get; set; }

    public AttributeData? Unique { get; set; }

    public AttributeData? Indexed { get; set; }

    public SchemaClass? Component { get; set; }

    public string? SlugSource { get; set; }

    public bool IsRelation { get; set; }

    // A relation whose target the host discovers as a collection or singleton.
    public bool TargetKnown { get; set; }

    public string? Reverse { get; set; }
}

// A finding of the reader, reported by the analyzer at the most precise place that is in source.
internal readonly record struct Finding(DiagnosticDescriptor Rule, Location? Location, object[] Arguments);

// Reads a schema class from symbols the way the attribute reader of FluentCMS.Core does from reflection (spec 4.2), and
// says what that reader refuses. Only what the reader refuses with certainty is a finding: a class it does not discover,
// such as an abstract one, is left alone.
internal static class SchemaReader
{
    public const string ContentItem = "FluentCMS.Schema.ContentItem";

    private const string Schema = "FluentCMS.Schema.";
    private const string FieldAttribute = Schema + "FieldAttribute";

    private static readonly Dictionary<string, string> FieldTypes = new(StringComparer.Ordinal)
    {
        [Schema + "TextAttribute"] = "text",
        [Schema + "LongTextAttribute"] = "longText",
        [Schema + "RichTextAttribute"] = "richText",
        [Schema + "IntegerAttribute"] = "integer",
        [Schema + "DecimalAttribute"] = "decimal",
        [Schema + "BooleanAttribute"] = "boolean",
        [Schema + "DateAttribute"] = "date",
        [Schema + "DateTimeAttribute"] = "dateTime",
        [Schema + "EmailAttribute"] = "email",
        [Schema + "UrlAttribute"] = "url",
        [Schema + "SlugAttribute"] = "slug",
        [Schema + "SelectAttribute"] = "select",
        [Schema + "JsonAttribute"] = "json",
        [Schema + "RelationAttribute"] = "relation",
        [Schema + "ComponentAttribute"] = "component",
        [Schema + "BlocksAttribute"] = "blocks",
        [Schema + "MediaAttribute"] = "media",
    };

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        Schema + "RequiredAttribute", Schema + "LengthAttribute", Schema + "RangeAttribute", Schema + "RegularExpressionAttribute",
        Schema + "DisplayAttribute", Schema + "DefaultValueAttribute", Schema + "ReadOnlyAttribute", Schema + "HiddenAttribute",
        Schema + "LocalizableAttribute", Schema + "IndexedAttribute", Schema + "SearchableAttribute", Schema + "UniqueAttribute",
        Schema + "FormerNamesAttribute", Schema + "VisibleWhenAttribute", Schema + "RequiredWhenAttribute",
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

    public static List<(SchemaKind Kind, AttributeData Attribute)> KindsOf(ITypeSymbol? type)
    {
        List<(SchemaKind, AttributeData)> kinds = [];
        if (type is null)
        {
            return kinds;
        }

        foreach (var attribute in type.GetAttributes())
        {
            SchemaKind? kind = FullName(attribute.AttributeClass) switch
            {
                Schema + "CollectionAttribute" => SchemaKind.Collection,
                Schema + "SingletonAttribute" => SchemaKind.Singleton,
                Schema + "ComponentAttribute" => SchemaKind.Component,
                Schema + "BlockAttribute" => SchemaKind.Block,
                _ => null,
            };

            if (kind is { } found)
            {
                kinds.Add((found, attribute));
            }
        }

        return kinds;
    }

    public static bool IsContentKind(ITypeSymbol? type) =>
        KindsOf(type) is { Count: 1 } kinds && kinds[0].Kind is SchemaKind.Collection or SchemaKind.Singleton;

    // The class as the host discovers it: concrete, not generic, and with one kind. Null for any other class.
    public static SchemaClass? Discover(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || IsGeneric(type) || KindsOf(type) is not { Count: 1 } kinds)
        {
            return null;
        }

        var (kind, attribute) = kinds[0];

        var declared = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        var localized = kind is SchemaKind.Collection or SchemaKind.Singleton
            && attribute.NamedArguments.Any(argument => argument.Key == "Localized" && argument.Value.Value is true);
        return new SchemaClass(type, kind, declared ?? SchemaNames.ToKebabCase(type.Name), localized, attribute);
    }

    // A discovered class whose base the reader accepts, so that it reads its fields.
    public static SchemaClass? Read(INamedTypeSymbol type) =>
        Discover(type) is { } schema && schema.IsContentType == DerivesFrom(type, ContentItem) ? schema : null;

    public static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    // The fields the reader adds, in its order, with the findings of the properties it refuses.
    public static List<SchemaField> Fields(INamedTypeSymbol type, List<Finding>? findings)
    {
        List<SchemaField> fields = [];
        foreach (var property in Properties(type))
        {
            if (ReadField(property, findings) is { } field)
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    // The instance properties of the class and its bases below ContentItem, the base's first, each in declaration order.
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

    private static SchemaField? ReadField(IPropertySymbol property, List<Finding>? findings)
    {
        var attributes = property.GetAttributes();
        var location = $"{property.ContainingType.Name}.{property.Name}";
        var fieldAttributes = attributes.Where(attribute => DerivesFrom(attribute.AttributeClass, FieldAttribute)).ToList();

        if (fieldAttributes.Count == 0)
        {
            if (attributes.FirstOrDefault(attribute => Modifiers.Contains(FullName(attribute.AttributeClass))) is { } modifier)
            {
                findings?.Add(new Finding(Rules.NoFieldType, LocationOf(modifier), [location]));
            }

            return null;
        }

        if (fieldAttributes.Count > 1)
        {
            findings?.Add(new Finding(Rules.FieldTypesCombined, LocationOf(fieldAttributes[1]), [location, string.Join(", ", fieldAttributes.Select(a => a.AttributeClass?.Name))]));
            return null;
        }

        if (property.GetMethod is not { DeclaredAccessibility: Accessibility.Public } || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
        {
            findings?.Add(new Finding(Rules.NotPublic, property.Locations.FirstOrDefault(), [location]));
            return null;
        }

        var attribute = fieldAttributes[0];
        var field = new SchemaField(property, SchemaNames.ToCamelCase(property.Name), FieldTypes.TryGetValue(FullName(attribute.AttributeClass), out var type) ? type : null, attribute);
        var failed = !ReadFieldType(field, location, findings);

        foreach (var modifier in attributes)
        {
            switch (FullName(modifier.AttributeClass))
            {
                case Schema + "LocalizableAttribute":
                    field.Localizable = modifier;
                    break;
                case Schema + "UniqueAttribute":
                    field.Unique = modifier;
                    break;
                case Schema + "IndexedAttribute":
                    field.Indexed = modifier;
                    break;
            }
        }

        // [Text(MaxLength = n)] is a shorthand for the upper bound of [Length]; naming two different bounds is refused.
        var text = field.FieldType == "text" ? Named(attribute, "MaxLength") as int? : null;
        var length = attributes.FirstOrDefault(a => FullName(a.AttributeClass) == Schema + "LengthAttribute");
        if (text is { } max and not 0 && length is not null && Named(length, "Max") is int lengthMax && lengthMax != int.MaxValue && lengthMax != max)
        {
            findings?.Add(new Finding(Rules.TwoMaximumLengths, LocationOf(length), [location, max, lengthMax]));
            failed = true;
        }

        return failed ? null : field;
    }

    // Mirrors ReadFieldType of the attribute reader. False when the reader refuses the field.
    private static bool ReadFieldType(SchemaField field, string location, List<Finding>? findings)
    {
        var property = field.Property;
        var attribute = field.FieldAttribute;
        var (element, multiple) = Unwrap(property.Type);

        // [Select(null)] or [Blocks(null)] stops the reader with an exception rather than a code, so nothing is reported.
        if (field.FieldType is "select" or "blocks" && attribute.ConstructorArguments is { Length: 1 } arguments && arguments[0].IsNull)
        {
            return false;
        }

        switch (field.FieldType)
        {
            case "decimal":
            case "slug" when Named(attribute, "Source") is string:
                field.SlugSource = Named(attribute, "Source") as string;
                return true;

            case "select":
                var options = Array(attribute).Length;
                if (element.TypeKind == TypeKind.Enum && options > 0)
                {
                    findings?.Add(new Finding(Rules.EnumWithOptions, LocationOf(attribute), [location]));
                    return false;
                }

                if (element.TypeKind != TypeKind.Enum && options == 0)
                {
                    findings?.Add(new Finding(Rules.NoOptions, LocationOf(attribute), [location]));
                    return false;
                }

                field.Multiple = multiple;
                return true;

            case "relation":
                return ReadRelation(field, location, findings);

            case "component":
                if (element is INamedTypeSymbol { TypeKind: TypeKind.Class } component && KindsOf(component) is { Count: 1 } kinds && kinds[0].Kind == SchemaKind.Component)
                {
                    field.Component = Read(component);
                    field.Multiple = multiple;
                    return true;
                }

                findings?.Add(new Finding(Rules.NotAComponent, LocationOf(attribute), [location, element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
                return false;

            case "blocks":
                var blocks = Array(attribute);
                if (blocks.Length == 0)
                {
                    findings?.Add(new Finding(Rules.NoBlockTypes, LocationOf(attribute), [location]));
                    return false;
                }

                var accepted = true;
                foreach (var block in blocks.Select(value => value.Value).OfType<ITypeSymbol>())
                {
                    if (KindsOf(block) is not { Count: 1 } blockKinds || blockKinds[0].Kind != SchemaKind.Block)
                    {
                        findings?.Add(new Finding(Rules.NotABlock, LocationOf(attribute), [location, block.Name]));
                        accepted = false;
                    }
                }

                return accepted;

            case "media" when FullName(property.Type as INamedTypeSymbol) == "FluentCMS.Media.MediaRefList":
                field.Multiple = true;
                return true;

            // A collection stands for a list, which schema validation refuses on a type that holds a single value. Json
            // keeps its own shape.
            case not "json":
                field.Multiple = multiple;
                return true;

            default:
                return true;
        }
    }

    private static bool ReadRelation(SchemaField field, string location, List<Finding>? findings)
    {
        var type = NotNullable(field.Property.Type) as INamedTypeSymbol;
        var definition = type is { IsGenericType: true } ? FullName(type.ConstructedFrom) : null;
        if (definition is not (Schema + "Ref`1" or Schema + "RefList`1"))
        {
            findings?.Add(new Finding(Rules.NotAReference, LocationOf(field.FieldAttribute), [location]));
            return false;
        }

        var target = type!.TypeArguments[0];
        if (!IsContentKind(target))
        {
            findings?.Add(new Finding(Rules.RelationTargetNotContentType, TypeLocation(field.Property) ?? LocationOf(field.FieldAttribute), [target.Name, location]));
            return false;
        }

        // The reader takes a target marked as a content type, but the host knows only one it discovers; an abstract one
        // is refused by schema validation with the same code.
        if (target is not INamedTypeSymbol named || Discover(named) is null)
        {
            findings?.Add(new Finding(Rules.RelationTargetNotContentType, TypeLocation(field.Property) ?? LocationOf(field.FieldAttribute), [target.Name, location]));
        }
        else
        {
            field.TargetKnown = true;
        }

        field.IsRelation = true;
        field.Multiple = definition == Schema + "RefList`1";
        field.Reverse = Named(field.FieldAttribute, "Reverse") is string reverse ? SchemaNames.ToCamelCase(reverse) : null;
        return true;
    }

    // The values of a params array, such as the options of [Select] or the block types of [Blocks].
    private static ImmutableArray<TypedConstant> Array(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array
            ? attribute.ConstructorArguments[0].Values
            : ImmutableArray<TypedConstant>.Empty;

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

    public static object? Named(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value;
            }
        }

        return null;
    }

    public static bool DerivesFrom(INamedTypeSymbol? type, string baseName)
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

    public static string FullName(INamedTypeSymbol? type) =>
        type is null ? "" : type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;

    public static Location? LocationOf(AttributeData? attribute) => attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation();

    // The named argument of an attribute, such as Source of [Slug], or the attribute when it has none in source.
    public static Location? ArgumentLocation(AttributeData attribute, string name) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax { ArgumentList: { } arguments }
        && arguments.Arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == name) is { } found
            ? found.GetLocation()
            : LocationOf(attribute);

    private static Location? TypeLocation(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax declaration
            ? declaration.Type.GetLocation()
            : null;
}
