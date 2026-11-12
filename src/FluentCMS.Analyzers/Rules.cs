using Microsoft.CodeAnalysis;

namespace FluentCMS.Analyzers;

// FCMS0001 to FCMS0099 belong to the analyzers, FCMS0100 to FCMS0119 to the generator. A rule reports what schema
// validation refuses at start and names the FCMS12xx code it stands for; FCMS0060 is advice and stands for none.
// 0001-0019 attribute combinations, 0020-0029 names, 0030 slugs, 0040 relations, 0050 localization, 0060 queries.
internal static class Rules
{
    public const string HelpLinkBase = "https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md#";

    private const string Schema = "FluentCMS.Schema";
    private const string Query = "FluentCMS.Query";

    public static readonly DiagnosticDescriptor KindsCombined = new(
        "FCMS0001",
        "A class has more than one schema kind",
        "The class '{0}' has more than one of [Collection], [Singleton], [Component] and [Block]; the host refuses it with FCMS1215",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0001");

    public static readonly DiagnosticDescriptor WrongBase = new(
        "FCMS0002",
        "A schema class has the wrong base",
        "The {0} '{1}' {2} derive from ContentItem; the host refuses it with FCMS1215",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0002");

    public static readonly DiagnosticDescriptor PublishPolicyNotPublished = new(
        "FCMS0003",
        "A component or block has a publish policy",
        "The {0} '{1}' is not published, so it cannot have [PublishPolicy]; the host refuses it with FCMS1215",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0003");

    public static readonly DiagnosticDescriptor NoFieldType = new(
        "FCMS0004",
        "A property has schema attributes but no field type",
        "The property '{0}' has schema attributes but no field type attribute such as [Text]; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0004");

    public static readonly DiagnosticDescriptor FieldTypesCombined = new(
        "FCMS0005",
        "A property has more than one field type",
        "The property '{0}' has more than one field type attribute ({1}); the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0005");

    public static readonly DiagnosticDescriptor NotPublic = new(
        "FCMS0006",
        "A field property has no public getter and setter",
        "The property '{0}' needs a public getter and setter to be a field; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0006");

    public static readonly DiagnosticDescriptor EnumWithOptions = new(
        "FCMS0007",
        "A select field on an enum lists options",
        "The property '{0}' is an enum, so its members are the options and [Select] must not list any; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0007");

    public static readonly DiagnosticDescriptor NoOptions = new(
        "FCMS0008",
        "A select field lists no options",
        "The property '{0}' is not an enum, so [Select] must list its options; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0008");

    public static readonly DiagnosticDescriptor NotAReference = new(
        "FCMS0009",
        "A relation field is not a Ref<T> or a RefList<T>",
        "The relation property '{0}' must be a Ref<T> or a RefList<T>; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0009");

    public static readonly DiagnosticDescriptor NotAComponent = new(
        "FCMS0010",
        "A component field holds a class that is not a component",
        "The component field '{0}' has type '{1}', which is not marked with [Component]; the host refuses it with FCMS1210",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0010");

    public static readonly DiagnosticDescriptor NoBlockTypes = new(
        "FCMS0011",
        "A blocks field lists no block types",
        "The blocks field '{0}' must list the block types it accepts; the host refuses it with FCMS1211",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0011");

    public static readonly DiagnosticDescriptor NotABlock = new(
        "FCMS0012",
        "A blocks field lists a class that is not a block",
        "The blocks field '{0}' lists '{1}', which is not marked with [Block]; the host refuses it with FCMS1210",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0012");

    public static readonly DiagnosticDescriptor TwoMaximumLengths = new(
        "FCMS0013",
        "A field sets two different maximum lengths",
        "The property '{0}' sets the maximum length to {1} and to {2}; the host refuses it with FCMS1216",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0013");

    public static readonly DiagnosticDescriptor ListOfSingleValue = new(
        "FCMS0014",
        "A field type that holds a single value is a list",
        "The {0} field '{1}' is a list, but a {0} field holds a single value; the host refuses it with FCMS1211",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0014");

    public static readonly DiagnosticDescriptor UniqueWithoutColumn = new(
        "FCMS0015",
        "A unique field has no column to index",
        "The field '{0}' is unique, but a {1} field {2}; the host refuses it with FCMS1211",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0015");

    public static readonly DiagnosticDescriptor ReverseOutsideContentType = new(
        "FCMS0016",
        "A reverse relation is declared outside a content type",
        "The relation '{0}' declares a reverse side, but only a field of a collection or singleton may; the host refuses it with FCMS1217",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0016");

    public static readonly DiagnosticDescriptor ReservedFieldName = new(
        "FCMS0020",
        "A field name is reserved",
        "The field '{0}' {1}; the host refuses it with FCMS1206",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0020");

    public static readonly DiagnosticDescriptor ReservedTypeName = new(
        "FCMS0021",
        "A type name maps to a system table",
        "The {0} '{1}' needs the table {2}, which a system table already uses; the host refuses it with FCMS1206",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0021");

    public static readonly DiagnosticDescriptor TypeNameTooLong = new(
        "FCMS0022",
        "A type name is too long",
        "The {0} '{1}' {2}; the host refuses it with FCMS1218",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0022");

    public static readonly DiagnosticDescriptor FieldNameTooLong = new(
        "FCMS0023",
        "A field name is too long",
        "The field '{0}' {1}; the host refuses it with FCMS1218",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0023");

    public static readonly DiagnosticDescriptor SlugSourceMissing = new(
        "FCMS0030",
        "A slug source is not another field",
        "The slug source '{0}' of '{1}' is not another field of '{2}'; the host refuses it with FCMS1205",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0030");

    public static readonly DiagnosticDescriptor RelationTargetNotContentType = new(
        "FCMS0040",
        "A relation target is not a content type",
        "The relation target '{0}' of '{1}' is not a concrete class marked with [Collection] or [Singleton]; the host refuses it with FCMS1204",
        Schema,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0040");

    public static readonly DiagnosticDescriptor LocalizableOnUnlocalizedType = new(
        "FCMS0050",
        "A localizable field is in a type that is not localized",
        "The field '{0}' is localizable, but {1}; the host refuses it with FCMS1208",
        Schema,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0050");

    public static readonly DiagnosticDescriptor FilterNotIndexed = new(
        "FCMS0060",
        "A filtered or sorted field is not indexed",
        "The field '{0}' of the {1} '{2}' is filtered or sorted here but is not [Indexed], so the query reads every row of the type",
        Query,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "fcms0060");
}
