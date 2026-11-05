using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// FluentCms:RateLimits. Every limit is a number of requests per minute for one caller.
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    // Per API key, or per client address for a caller without one. A key's own limit replaces it.
    public DeliveryRateLimitOptions Delivery { get; } = new() { PermitLimit = 600 };

    // Per signed-in principal.
    public RateLimitPolicyOptions Management { get; } = new() { PermitLimit = 300 };

    // Per client address, on /connect and /account: sign-in, token and password endpoints.
    public RateLimitPolicyOptions Auth { get; } = new() { PermitLimit = 20 };

    // Per client address on every FluentCMS route but /health, and it runs before authentication. It is what keeps a flood of
    // requests with a key or a sign-in from reaching the key lookup and the token endpoint.
    public RateLimitPolicyOptions PreAuth { get; } = new() { PermitLimit = 6000 };

    internal void Validate()
    {
        Delivery.Validate(nameof(Delivery));
        Management.Validate(nameof(Management));
        Auth.Validate(nameof(Auth));
        PreAuth.Validate(nameof(PreAuth));

        // The coarse limit holds all the callers behind one address together, so a key may not be allowed more than it.
        if (Delivery.MaxKeyLimit > PreAuth.PermitLimit)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(Delivery)}:{nameof(Delivery.MaxKeyLimit)} is {Delivery.MaxKeyLimit}; it must not be more than {nameof(PreAuth)}:{nameof(PreAuth.PermitLimit)}, {PreAuth.PermitLimit}, which limits every caller behind one address.");
        }
    }
}

public class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }

    internal virtual void Validate(string key)
    {
        if (PermitLimit < 1)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(PermitLimit)} is {PermitLimit}; it must be at least 1.");
        }
    }
}

public sealed class DeliveryRateLimitOptions : RateLimitPolicyOptions
{
    // The most a key may be given by its own limit, so that a key cannot be set to no limit at all.
    public int MaxKeyLimit { get; set; } = 5000;

    internal override void Validate(string key)
    {
        base.Validate(key);
        if (MaxKeyLimit < PermitLimit)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(MaxKeyLimit)} is {MaxKeyLimit}; it must be at least {nameof(PermitLimit)}, {PermitLimit}.");
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
