using FluentCMS.Core.Identity;

namespace FluentCMS.Host.Identity;

internal sealed class HttpContextInitiator(IHttpContextAccessor accessor) : ICurrentInitiator
{
    public Initiator? Current => ClaimsInitiator.From(accessor.HttpContext?.User);
}

// The stdio host has no caller until F-MCP authenticates agent credentials.
internal sealed class NoInitiator : ICurrentInitiator
{
    public Initiator? Current => null;
}
