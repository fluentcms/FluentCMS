using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Names the surface an endpoint belongs to (delivery, management, connect, account, health, docs, or a plugin's name), for
// the request counter.
internal sealed record FluentCmsSurfaceMetadata(string Name)
{
    // The surfaces of core that the middleware treats apart are told by these instances, since a plugin's surface is its
    // name and a plugin named health, delivery or docs has a surface of that name.
    public static readonly FluentCmsSurfaceMetadata Health = new("health");

    public static readonly FluentCmsSurfaceMetadata Delivery = new("delivery");

    // The OpenAPI document and the Scalar UI that MapFluentCmsOpenApi maps.
    public static readonly FluentCmsSurfaceMetadata Docs = new("docs");

    public static bool IsHealth(Endpoint? endpoint) => Is(endpoint, Health);

    public static bool IsDelivery(Endpoint? endpoint) => Is(endpoint, Delivery);

    public static bool IsDocs(Endpoint? endpoint) => Is(endpoint, Docs);

    private static bool Is(Endpoint? endpoint, FluentCmsSurfaceMetadata surface) =>
        ReferenceEquals(endpoint?.Metadata.GetMetadata<FluentCmsSurfaceMetadata>(), surface);
}
