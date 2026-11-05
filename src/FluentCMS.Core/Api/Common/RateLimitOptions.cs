using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// FluentCms:RateLimits. Every limit is a number of requests per minute for one caller.
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    // Per API key, or per client address for a caller without one. A key's own limit replaces it.
    public RateLimitPolicyOptions Delivery { get; } = new() { PermitLimit = 600 };

    // Per signed-in principal.
    public RateLimitPolicyOptions Management { get; } = new() { PermitLimit = 300 };

    // Per client address, on /connect and /account: sign-in, token and password endpoints.
    public RateLimitPolicyOptions Auth { get; } = new() { PermitLimit = 20 };

    internal void Validate()
    {
        Delivery.Validate(nameof(Delivery));
        Management.Validate(nameof(Management));
        Auth.Validate(nameof(Auth));
    }
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }

    internal void Validate(string key)
    {
        if (PermitLimit < 1)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(PermitLimit)} is {PermitLimit}; it must be at least 1.");
        }
    }
}

// FluentCms:Limits.
public sealed class RequestLimitsOptions
{
    public const string Section = "Limits";

    // The body of a request to a FluentCMS endpoint, in bytes.
    [Range(1L, long.MaxValue)]
    public long MaxBodySize { get; set; } = 2 * 1024 * 1024;
}
