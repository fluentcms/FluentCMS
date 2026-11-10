using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.GraphQL;

// Event ids 9320 to 9329. A line never holds a query, a variable or a value of content.
internal static partial class GraphQLLog
{
    [LoggerMessage(EventId = 9320, Level = LogLevel.Information, Message = "The GraphQL schema has {ContentTypes} content types, {Contributors} schema contributors and {PersistedQueries} persisted queries.")]
    public static partial void SchemaBuilt(ILogger logger, int contentTypes, int contributors, int persistedQueries);

    [LoggerMessage(EventId = 9321, Level = LogLevel.Error, Message = "The GraphQL field {Field} failed and was answered as {Code}.")]
    public static partial void FieldFailed(ILogger logger, Exception exception, string field, string code);

    [LoggerMessage(EventId = 9322, Level = LogLevel.Information, Message = "A GraphQL request was refused before it ran: {Code}.")]
    public static partial void Refused(ILogger logger, string code);
}
