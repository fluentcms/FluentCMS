using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Names the surface an endpoint belongs to (delivery, management, connect, account, health, or a plugin's name), for the
// request counter.
internal sealed record FluentCmsSurfaceMetadata(string Name)
{
    // The probes under /health. They are told apart by this instance, since a plugin named health has a surface of that name.
    public static readonly FluentCmsSurfaceMetadata Health = new("health");

    public static bool IsHealth(Endpoint? endpoint) => ReferenceEquals(endpoint?.Metadata.GetMetadata<FluentCmsSurfaceMetadata>(), Health);
}
