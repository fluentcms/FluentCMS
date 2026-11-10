namespace FluentCMS.Plugins.GraphQL;

// The GraphQL plugin has the codes from FCMS1640 up to 1659. Each is added with AddErrorCode in GraphQLPlugin.
internal static class GraphQLErrorCodes
{
    public const string InvalidOptions = "FCMS1640";
    public const string SchemaConflict = "FCMS1641";
    public const string InvalidArguments = "FCMS1642";
    public const string IntrospectionNotAllowed = "FCMS1643";
    public const string DepthExceeded = "FCMS1644";
    public const string CostExceeded = "FCMS1645";
    public const string OnlyPersistedQueries = "FCMS1646";
    public const string PersistedQueryNotFound = "FCMS1647";
    public const string Timeout = "FCMS1648";
}

// The codes of the REST APIs that this surface answers where REST would: a locale or an id that is not one, and a field or
// a type the principal may not read.
internal static class RestErrorCodes
{
    public const string InvalidParameter = "FCMS2201";
    public const string UnknownField = "FCMS2202";
    public const string InvalidValue = "FCMS2204";
    public const string NotFound = "FCMS3104";
}
