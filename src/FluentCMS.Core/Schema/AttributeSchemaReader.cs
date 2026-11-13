using System.Reflection;
using FluentCMS.Media;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// Turns a class and its attributes into the calls a host could make on the builder by hand.
internal sealed class AttributeSchemaReader(SchemaBuilder builder)
{
    private const BindingFlags ConditionMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

    private static readonly Type[] CollectionTypes =
    [
        typeof(List<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>),
    ];

    public static bool IsSchemaClass(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && KindsOf(type).Count > 0;

    public void Read(Type type)
    {
        if (!builder.Track(type))
        {
            return;
        }

        var kinds = KindsOf(type);
        if (kinds.Count != 1)
        {
            Report(SchemaErrorCodes.InvalidType, kinds.Count == 0
                ? $"class {type.Name} is not marked with [Collection], [Singleton], [Component] or [Block]"
                : $"class {type.Name} has more than one of [Collection], [Singleton], [Component] and [Block]");
            return;
        }

        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
        {
            Report(SchemaErrorCodes.InvalidType, $"{type.Name} must be a concrete, non-generic class");
            return;
        }

        var (kind, declaredName, localized, scope) = kinds[0];
        var isContentType = kind is SchemaTypeKind.Collection or SchemaTypeKind.Singleton;
        if (isContentType != typeof(ContentItem).IsAssignableFrom(type))
        {
            Report(SchemaErrorCodes.InvalidType, isContentType
                ? $"{kind.ToString().ToLowerInvariant()} {type.Name} must derive from ContentItem"
                : $"{kind.ToString().ToLowerInvariant()} {type.Name} must not derive from ContentItem");
            return;
        }

        builder.AddType(declaredName ?? SchemaNaming.ToKebabCase(type.Name), kind, type, t =>
        {
            if (isContentType)
            {
                t.Localized(localized);
                t.Scope(scope);
                if (type.GetCustomAttribute<PublishPolicyAttribute>(inherit: false) is { } policy)
                {
                    if (policy.MaxVersions < 1)
                    {
                        Report(SchemaErrorCodes.InvalidType, $"{type.Name} must keep at least one version");
                    }
                    else if (policy.TrashRetentionDays is < 1 or > PublishPolicyAttribute.MaxTrashRetentionDays)
                    {
                        Report(SchemaErrorCodes.InvalidType, $"{type.Name} must keep its trash for 1 to {PublishPolicyAttribute.MaxTrashRetentionDays} days");
                    }
                    else
                    {
                        t.PublishPolicy(new PublishPolicyOptions { UnpublishedReferences = policy.UnpublishedReferences, MaxVersions = policy.MaxVersions, TrashRetentionDays = policy.TrashRetentionDays });
                    }
                }
            }
            else if (type.GetCustomAttribute<PublishPolicyAttribute>(inherit: false) is not null)
            {
                Report(SchemaErrorCodes.InvalidType, $"{kind.ToString().ToLowerInvariant()} {type.Name} is not published, so it cannot have a publish policy");
            }

            foreach (var property in Properties(type))
            {
                ReadField(t, type, property);
            }
        });
    }

    private static List<(SchemaTypeKind Kind, string? Name, bool Localized, ContentScope Scope)> KindsOf(Type type)
    {
        List<(SchemaTypeKind, string?, bool, ContentScope)> kinds = [];

        if (type.GetCustomAttribute<CollectionAttribute>(inherit: false) is { } collection)
        {
            kinds.Add((SchemaTypeKind.Collection, collection.Name, collection.Localized, collection.Scope));
        }

        if (type.GetCustomAttribute<SingletonAttribute>(inherit: false) is { } singleton)
        {
            kinds.Add((SchemaTypeKind.Singleton, singleton.Name, singleton.Localized, singleton.Scope));
        }

        if (type.GetCustomAttribute<ComponentAttribute>(inherit: false) is { } component)
        {
            kinds.Add((SchemaTypeKind.Component, component.Name, false, ContentScope.Site));
        }

        if (type.GetCustomAttribute<BlockAttribute>(inherit: false) is { } block)
        {
            kinds.Add((SchemaTypeKind.Block, block.Name, false, ContentScope.Site));
        }

        return kinds;
    }

    internal static string NameOf(Type type) =>
        KindsOf(type) is [{ Name: { } name }, ..] ? name : SchemaNaming.ToKebabCase(type.Name);

    private static IEnumerable<PropertyInfo> Properties(Type type)
    {
        List<Type> chain = [];
        for (var current = type; current is not null && current != typeof(object) && current != typeof(ContentItem); current = current.BaseType)
        {
            chain.Add(current);
        }

        chain.Reverse();

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var declaring in chain)
        {
            foreach (var property in declaring.GetProperties(flags).OrderBy(property => property.MetadataToken))
            {
                var accessor = property.GetMethod ?? property.SetMethod;
                var isOverride = accessor?.GetBaseDefinition().DeclaringType != property.DeclaringType;
                if (property.GetIndexParameters().Length == 0 && !isOverride)
                {
                    yield return property;
                }
            }
        }
    }

    private static (Type Element, bool Multiple) Unwrap(Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type.IsArray)
        {
            return (type.GetElementType()!, true);
        }

        if (type.IsGenericType && CollectionTypes.Contains(type.GetGenericTypeDefinition()))
        {
            return (type.GetGenericArguments()[0], true);
        }

        return (type, false);
    }

    private static bool IsSchemaModifier(object attribute) =>
        attribute is RequiredAttribute or LengthAttribute or RangeAttribute or RegularExpressionAttribute
            or DisplayAttribute or DefaultValueAttribute or ReadOnlyAttribute or HiddenAttribute
            or LocalizableAttribute or IndexedAttribute or SearchableAttribute or UniqueAttribute
            or FormerNamesAttribute or VisibleWhenAttribute or RequiredWhenAttribute;

    private void Report(string code, string message) => builder.Report(code, message);

    private void ReadField(ITypeBuilder typeBuilder, Type owner, PropertyInfo property)
    {
        var attributes = property.GetCustomAttributes(inherit: false);
        var location = $"{property.DeclaringType!.Name}.{property.Name}";
        var fieldAttributes = attributes.OfType<FieldAttribute>().ToList();

        if (fieldAttributes.Count == 0)
        {
            if (attributes.Any(IsSchemaModifier))
            {
                Report(SchemaErrorCodes.InvalidProperty, $"property {location} has schema attributes but no field type attribute such as [Text]");
            }

            return;
        }

        if (fieldAttributes.Count > 1)
        {
            Report(SchemaErrorCodes.InvalidProperty, $"property {location} has more than one field type attribute: {string.Join(", ", fieldAttributes.Select(a => a.GetType().Name))}");
            return;
        }

        if (property.GetMethod is not { IsPublic: true } || property.SetMethod is not { IsPublic: true })
        {
            Report(SchemaErrorCodes.InvalidProperty, $"property {location} needs a public getter and setter");
            return;
        }

        var fieldAttribute = fieldAttributes[0];
        List<Action<IFieldBuilder>> steps = [];
        var failed = false;

        void Fail(string code, string message)
        {
            Report(code, message);
            failed = true;
        }

        try
        {
            ReadFieldType(fieldAttribute, property, location, steps, Fail);
            ReadModifiers(attributes, owner, location, steps, Fail);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Fail(SchemaErrorCodes.InvalidProperty, $"property {location}: {exception.Message}");
        }

        if (failed)
        {
            return;
        }

        try
        {
            typeBuilder.Field(SchemaNaming.ToCamelCase(property.Name), fieldAttribute.FieldType, field =>
            {
                foreach (var step in steps)
                {
                    step(field);
                }
            });
        }
        catch (ArgumentException exception)
        {
            Report(SchemaErrorCodes.InvalidProperty, $"property {location}: {exception.Message}");
        }
    }

    private void ReadFieldType(FieldAttribute attribute, PropertyInfo property, string location, List<Action<IFieldBuilder>> steps, Action<string, string> fail)
    {
        var (element, multiple) = Unwrap(property.PropertyType);
        element = Nullable.GetUnderlyingType(element) ?? element;

        switch (attribute)
        {
            case DecimalAttribute decimalAttribute:
                if (decimalAttribute.Precision != 0)
                {
                    steps.Add(f => f.Setting(FieldSettings.Precision, decimalAttribute.Precision));
                }

                if (decimalAttribute.Scale != 0)
                {
                    steps.Add(f => f.Setting(FieldSettings.Scale, decimalAttribute.Scale));
                }

                break;

            case SlugAttribute { Source: { } source }:
                steps.Add(f => f.Setting(FieldSettings.Source, SchemaNaming.ToCamelCase(source)));
                break;

            case SelectAttribute select:
                if (element.IsEnum && select.Options.Count > 0)
                {
                    fail(SchemaErrorCodes.InvalidProperty, $"property {location} is an enum, so its members are the options and [Select] must not list any");
                }
                else if (element.IsEnum)
                {
                    steps.Add(f => f.Setting(FieldSettings.Options, Enum.GetNames(element).Select(SchemaNaming.ToCamelCase).ToArray()));
                }
                else if (select.Options.Count > 0)
                {
                    steps.Add(f => f.Setting(FieldSettings.Options, select.Options));
                }
                else
                {
                    fail(SchemaErrorCodes.InvalidProperty, $"property {location} is not an enum, so [Select] must list its options");
                }

                if (multiple)
                {
                    steps.Add(f => f.Multiple());
                }

                break;

            case RelationAttribute relation:
                ReadRelation(relation, property, location, steps, fail);
                break;

            case ComponentAttribute:
                if (element.IsClass && KindsOf(element) is [{ Kind: SchemaTypeKind.Component }])
                {
                    Read(element);
                    var component = NameOf(element);
                    steps.Add(f => f.Setting(FieldSettings.Component, component));
                    if (multiple)
                    {
                        steps.Add(f => f.Multiple());
                    }
                }
                else
                {
                    fail(SchemaErrorCodes.ComponentOrBlockMissing, $"component field {location} has type {element.Name}, which is not marked with [Component]");
                }

                break;

            case BlocksAttribute blocks:
                ReadBlocks(blocks, location, steps, fail);
                break;

            case MediaAttribute when property.PropertyType == typeof(MediaRefList):
                steps.Add(f => f.Multiple());
                break;

            // A collection stands for a list; the validator rejects it on a type that holds a single value. Json keeps its own shape.
            case not JsonAttribute when multiple:
                steps.Add(f => f.Multiple());
                break;
        }
    }

    private static void ReadRelation(RelationAttribute relation, PropertyInfo property, string location, List<Action<IFieldBuilder>> steps, Action<string, string> fail)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        if (definition != typeof(Ref<>) && definition != typeof(RefList<>))
        {
            fail(SchemaErrorCodes.InvalidProperty, $"relation property {location} must be a Ref<T> or a RefList<T>");
            return;
        }

        var target = type.GetGenericArguments()[0];
        if (KindsOf(target) is not [{ Kind: SchemaTypeKind.Collection or SchemaTypeKind.Singleton }])
        {
            fail(SchemaErrorCodes.RelationTargetMissing, $"relation target {target.Name} of {location} is not marked with [Collection] or [Singleton]");
            return;
        }

        var reverse = relation.Reverse is null ? null : SchemaNaming.ToCamelCase(relation.Reverse);
        steps.Add(f => f.Relation(NameOf(target), reverse, relation.Ordered));
        if (definition == typeof(RefList<>))
        {
            steps.Add(f => f.Multiple());
        }
    }

    private void ReadBlocks(BlocksAttribute blocks, string location, List<Action<IFieldBuilder>> steps, Action<string, string> fail)
    {
        if (blocks.BlockTypes.Count == 0)
        {
            fail(SchemaErrorCodes.InvalidField, $"blocks field {location} must list the block types it accepts");
            return;
        }

        List<string> names = [];
        foreach (var blockType in blocks.BlockTypes)
        {
            if (KindsOf(blockType) is [{ Kind: SchemaTypeKind.Block }])
            {
                Read(blockType);
                names.Add(NameOf(blockType));
            }
            else
            {
                fail(SchemaErrorCodes.ComponentOrBlockMissing, $"blocks field {location} lists {blockType.Name}, which is not marked with [Block]");
            }
        }

        steps.Add(f => f.Setting(FieldSettings.Blocks, names));
    }

    private static void ReadModifiers(object[] attributes, Type owner, string location, List<Action<IFieldBuilder>> steps, Action<string, string> fail)
    {
        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case RequiredAttribute:
                    steps.Add(f => f.Required());
                    break;
                case UniqueAttribute:
                    steps.Add(f => f.Unique());
                    break;
                case IndexedAttribute:
                    steps.Add(f => f.Indexed());
                    break;
                case SearchableAttribute:
                    steps.Add(f => f.Searchable());
                    break;
                case ReadOnlyAttribute:
                    steps.Add(f => f.IsReadOnly());
                    break;
                case HiddenAttribute:
                    steps.Add(f => f.Hidden());
                    break;
                case LocalizableAttribute:
                    steps.Add(f => f.Localizable());
                    break;
                case RegularExpressionAttribute expression:
                    steps.Add(f => f.Pattern(expression.Pattern));
                    break;
                case DisplayAttribute display:
                    steps.Add(f => f.Display(new FieldDisplayDescription { Label = display.Label, Help = display.Help, Placeholder = display.Placeholder, Group = display.Group, Tab = display.Tab, Width = display.Width }));
                    break;
                case DefaultValueAttribute defaultValue:
                    steps.Add(f => f.DefaultValue(defaultValue.Value));
                    break;
                case FormerNamesAttribute formerNames:
                    steps.Add(f => f.FormerNames([.. formerNames.Names.Select(SchemaNaming.ToCamelCase)]));
                    break;
                case RangeAttribute range:
                    var (min, max) = (Bound(range.Min), Bound(range.Max));
                    steps.Add(f => f.Range(min, max));
                    break;
                case VisibleWhenAttribute visibleWhen:
                    if (ResolveCondition(owner, visibleWhen.ConditionName, location, fail) is { } visible)
                    {
                        steps.Add(f => f.VisibleWhen(visible));
                    }

                    break;
                case RequiredWhenAttribute requiredWhen:
                    if (ResolveCondition(owner, requiredWhen.ConditionName, location, fail) is { } required)
                    {
                        steps.Add(f => f.RequiredWhen(required));
                    }

                    break;
            }
        }

        ReadLength(attributes, location, steps, fail);
    }

    // [Text(MaxLength = n)] is a shorthand for the upper bound of [Length]; naming two different bounds is an error.
    private static void ReadLength(object[] attributes, string location, List<Action<IFieldBuilder>> steps, Action<string, string> fail)
    {
        int? min = null;
        int? max = attributes.OfType<TextAttribute>().FirstOrDefault() is { MaxLength: not 0 } text ? text.MaxLength : null;

        if (attributes.OfType<LengthAttribute>().FirstOrDefault() is { } length)
        {
            min = length.Min > 0 ? length.Min : null;
            if (length.Max != int.MaxValue)
            {
                if (max is not null && max != length.Max)
                {
                    fail(SchemaErrorCodes.InvalidProperty, $"property {location} sets the maximum length to {max} and to {length.Max}");
                }

                max = length.Max;
            }
        }

        if (min is not null || max is not null)
        {
            steps.Add(f => f.Length(min, max));
        }
    }

    private static decimal? Bound(double value) => double.IsInfinity(value) ? null : (decimal)value;

    private static Condition? ResolveCondition(Type owner, string memberName, string location, Action<string, string> fail)
    {
        object? value;
        try
        {
            var property = owner.GetProperty(memberName, ConditionMembers);
            var field = property is null ? owner.GetField(memberName, ConditionMembers) : null;
            if (property is null && field is null)
            {
                fail(SchemaErrorCodes.InvalidCondition, $"condition {memberName} of {location} is not a static property or field of {owner.Name}");
                return null;
            }

            value = property is not null ? property.GetValue(null) : field!.GetValue(null);
        }
        catch (TargetInvocationException exception)
        {
            fail(SchemaErrorCodes.InvalidCondition, $"condition {memberName} of {location} threw {exception.InnerException?.GetType().Name}: {exception.InnerException?.Message}");
            return null;
        }

        if (value is not Condition condition)
        {
            fail(SchemaErrorCodes.InvalidCondition, $"condition {memberName} of {location} is not a Condition");
            return null;
        }

        return condition;
    }
}
