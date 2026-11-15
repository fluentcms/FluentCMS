using Microsoft.AspNetCore.Builder;

namespace FluentCMS.Core.Api.OpenApi;

// A parameter of the document's components that a route beside content reads, such as the If-Match of a settings write.
internal sealed record SharedParameter(string Component);

internal static class SharedParameterExtensions
{
    public static RouteHandlerBuilder WithParameter(this RouteHandlerBuilder builder, string component) =>
        builder.WithMetadata(new SharedParameter(component));
}
