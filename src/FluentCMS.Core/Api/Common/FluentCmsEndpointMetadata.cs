namespace FluentCMS.Core.Api.Common;

// Marks the endpoints MapFluentCms creates, so that the middleware of UseFluentCms leaves a host's own endpoints alone.
internal sealed class FluentCmsEndpointMetadata
{
    public static readonly FluentCmsEndpointMetadata Instance = new();
}

// Marks the hosted pages, whose responses also carry a Content-Security-Policy.
internal sealed class HostedPageMetadata
{
    public static readonly HostedPageMetadata Instance = new();
}
