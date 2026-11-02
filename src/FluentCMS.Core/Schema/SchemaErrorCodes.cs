namespace FluentCMS.Core.Schema;

internal static class SchemaErrorCodes
{
    public const string DuplicateType = "FCMS1201";
    public const string DuplicateField = "FCMS1202";
    public const string FieldTypeNotRegistered = "FCMS1203";
    public const string RelationTargetMissing = "FCMS1204";
    public const string SlugSourceMissing = "FCMS1205";
    public const string ReservedName = "FCMS1206";
    public const string NestingTooDeep = "FCMS1207";
    public const string LocalizableOnUnlocalizedType = "FCMS1208";
    public const string DuplicateReverseRelation = "FCMS1209";
    public const string ComponentOrBlockMissing = "FCMS1210";
    public const string InvalidField = "FCMS1211";
    public const string InvalidCondition = "FCMS1212";
    public const string InvalidSchemaFile = "FCMS1213";
    public const string InvalidName = "FCMS1214";
    public const string InvalidType = "FCMS1215";
    public const string InvalidProperty = "FCMS1216";
    public const string ReverseRelationOutsideContentType = "FCMS1217";
}
