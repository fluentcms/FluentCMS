using FluentCMS.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

// Bound from FluentCms:Webhooks.
public sealed class WebhooksOptions
{
    public const string Section = "FluentCms:Webhooks";

    // Lets deliveries reach loopback, private, link-local and the other addresses that are not public. Off by default, and
    // reported as an insecure default in Production.
    public bool AllowPrivateNetworks { get; set; }

    // How long an attempt waits for the receiver's answer.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    // How long the delivery log keeps an attempt.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    // Workers of the webhooks queue on this instance, unless FluentCms:Jobs:Queues:webhooks sets them.
    public int Workers { get; set; } = 2;

    // How long the secret a rotation replaces keeps signing deliveries beside the new one. Zero ends it at the rotation.
    public TimeSpan SecretOverlap { get; set; } = TimeSpan.FromHours(24);

    // Disables a webhook when a delivery to it dies and every attempt to it for at least this long has failed. Unset, the
    // default, never disables one. The delivery log is the record of the attempts, so it is at most Retention.
    public TimeSpan? DisableAfter { get; set; }
}

internal sealed class WebhookInsecureDefaults(IOptions<WebhooksOptions> options, IHostEnvironment environment) : IInsecureDefaultSource
{
    public const string PrivateNetworks = "webhooks-private-networks";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (environment.IsProduction() && options.Value.AllowPrivateNetworks)
        {
            yield return new InsecureDefault
            {
                Id = PrivateNetworks,
                Message = $"{WebhooksOptions.Section}:{nameof(WebhooksOptions.AllowPrivateNetworks)} is on in Production, so a webhook can deliver to loopback, private and cloud metadata addresses. Turn it off unless every receiver is internal.",
            };
        }
    }
}
