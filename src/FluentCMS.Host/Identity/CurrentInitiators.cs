using FluentCMS.Core.Identity;

namespace FluentCMS.Host.Identity;

internal sealed class HttpContextInitiator(IHttpContextAccessor accessor) : ICurrentInitiator
{
    public Initiator? Current => ClaimsInitiator.From(accessor.HttpContext?.User);
}
