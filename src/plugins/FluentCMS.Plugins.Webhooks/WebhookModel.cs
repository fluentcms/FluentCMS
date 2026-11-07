using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Webhooks;

// A subscription of a site. Secret is protected with Data Protection; events, types and headers are JSON.
internal sealed class Webhook
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public string Secret { get; set; } = "";

    public string Events { get; set; } = "[]";

    public string Types { get; set; } = "[]";

    public string Headers { get; set; } = "{}";

    public bool Enabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }
}

// One attempt of a delivery. The attempts of a delivery share its DeliveryId, which is the X-FluentCMS-Delivery header,
// and each holds the body it sent, so that a delivery can be sent again after its job is gone.
internal sealed class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid DeliveryId { get; set; }

    public Guid WebhookId { get; set; }

    public Guid EventId { get; set; }

    public string Event { get; set; } = "";

    public int Attempt { get; set; }

    public string State { get; set; } = "";

    public int? StatusCode { get; set; }

    public long DurationMs { get; set; }

    public string RequestBody { get; set; } = "";

    public string? ResponseExcerpt { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset? NextRetryAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal static class DeliveryStates
{
    public const string Succeeded = "succeeded";

    // Failed, and another attempt follows at NextRetryAt.
    public const string Failed = "failed";

    // Failed, and no attempt follows.
    public const string Dead = "dead";
}

internal sealed class WebhookModel : IPluginModelContributor
{
    public const string WebhookTable = "cms_webhooks_subscription";
    public const string DeliveryTable = "cms_webhooks_delivery";

    public const int NameMaxLength = 100;
    public const int UrlMaxLength = 2048;
    public const int EventMaxLength = 64;
    public const int ErrorMaxLength = 1000;
    public const int ResponseExcerptMaxLength = 1024;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Webhook>(webhook =>
        {
            webhook.ToTable(WebhookTable);
            webhook.HasKey(item => item.Id);
            webhook.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            webhook.Property(item => item.SiteId).HasColumnName("site_id");
            webhook.Property(item => item.Name).HasColumnName("name").HasMaxLength(NameMaxLength).IsRequired();
            webhook.Property(item => item.Url).HasColumnName("url").HasMaxLength(UrlMaxLength).IsRequired();
            webhook.Property(item => item.Secret).HasColumnName("secret").IsRequired();
            webhook.Property(item => item.Events).HasColumnName("events").IsRequired();
            webhook.Property(item => item.Types).HasColumnName("types").IsRequired();
            webhook.Property(item => item.Headers).HasColumnName("headers").IsRequired();
            webhook.Property(item => item.Enabled).HasColumnName("enabled");
            webhook.Property(item => item.CreatedAt).HasColumnName("created_at");
            webhook.Property(item => item.CreatedBy).HasColumnName("created_by");
            webhook.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            webhook.Property(item => item.UpdatedBy).HasColumnName("updated_by");
            webhook.HasIndex(item => new { item.SiteId, item.Name }).IsUnique().HasDatabaseName("ux_cms_webhooks_subscription_site_name");
        });

        modelBuilder.Entity<WebhookDelivery>(delivery =>
        {
            delivery.ToTable(DeliveryTable);
            delivery.HasKey(item => item.Id);
            delivery.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            delivery.Property(item => item.DeliveryId).HasColumnName("delivery_id");
            delivery.Property(item => item.WebhookId).HasColumnName("webhook_id");
            delivery.Property(item => item.EventId).HasColumnName("event_id");
            delivery.Property(item => item.Event).HasColumnName("event").HasMaxLength(EventMaxLength).IsRequired();
            delivery.Property(item => item.Attempt).HasColumnName("attempt");
            delivery.Property(item => item.State).HasColumnName("state").HasMaxLength(16).IsRequired();
            delivery.Property(item => item.StatusCode).HasColumnName("status_code");
            delivery.Property(item => item.DurationMs).HasColumnName("duration_ms");
            delivery.Property(item => item.RequestBody).HasColumnName("request_body").IsRequired();
            delivery.Property(item => item.ResponseExcerpt).HasColumnName("response_excerpt").HasMaxLength(ResponseExcerptMaxLength);
            delivery.Property(item => item.Error).HasColumnName("error").HasMaxLength(ErrorMaxLength);
            delivery.Property(item => item.NextRetryAt).HasColumnName("next_retry_at");
            delivery.Property(item => item.CreatedAt).HasColumnName("created_at");
            delivery.HasIndex(item => new { item.DeliveryId, item.Attempt }).IsUnique().HasDatabaseName("ux_cms_webhooks_delivery_attempt");
            delivery.HasIndex(item => new { item.WebhookId, item.CreatedAt }).HasDatabaseName("ix_cms_webhooks_delivery_webhook");
            delivery.HasIndex(item => item.CreatedAt).HasDatabaseName("ix_cms_webhooks_delivery_created_at");
        });
    }
}
