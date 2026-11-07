using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

// Without a public origin, reset and invitation links follow the request's Host, so a forged one sends the token elsewhere.
// Unlike the signing key check it refuses in Production only, so that the report of other environments stays as it was.
internal sealed class PublicOriginCheck(SecurityOptions options, PublicOrigin origin, IHostEnvironment? environment = null) : IProductionStartupCheck
{
    public const string Key = $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{nameof(SecurityOptions.AllowRequestOrigin)}";

    public StartupRefusal? Check() => environment?.IsProduction() == true && origin.Base is null && !options.AllowRequestOrigin
        ? new StartupRefusal(
            HostErrorCodes.PublicOriginUnsetInProduction,
            $"Production builds password reset and invitation links from the request's Host header, because {FluentCmsOptions.Section}:{PublicOrigin.Key} is not set. Set it to the address users reach the instance at, such as https://cms.example.com, or set {Key} to true when AllowedHosts or the proxy in front lets through only the instance's own host names.")
        : null;
}
