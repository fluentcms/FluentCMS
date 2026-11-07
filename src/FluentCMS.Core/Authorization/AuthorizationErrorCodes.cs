namespace FluentCMS.Core.Authorization;

internal static class AuthorizationErrorCodes
{
    public const string DuplicatePermission = "FCMS1401";
    public const string ReservedPermission = "FCMS1402";
    public const string UnsupportedRowRule = "FCMS1403";
    public const string DuplicateRowRule = "FCMS1404";
    public const string InvalidRowRule = "FCMS1405";
    public const string FieldNotWritable = "FCMS1406";
    public const string NoOperation = "FCMS1408";
}
