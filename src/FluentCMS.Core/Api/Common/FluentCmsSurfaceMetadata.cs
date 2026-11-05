namespace FluentCMS.Core.Api.Common;

// Names the surface an endpoint belongs to (delivery, management, connect, account, health), for the request counter.
internal sealed record FluentCmsSurfaceMetadata(string Name);
