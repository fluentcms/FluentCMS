using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Webhooks;

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDeliveryRow>
{
    public const int MaxErrorLength = 2000;

    public void Configure(EntityTypeBuilder<WebhookDeliveryRow> row)
    {
        row.ToTable("WebhookDeliveries");

        row.HasKey(delivery => delivery.Id);
        row.Property(delivery => delivery.Id).ValueGeneratedNever();
        row.Property(delivery => delivery.DeliveryId).HasMaxLength(64).IsRequired();
        row.Property(delivery => delivery.Event).HasMaxLength(64).IsRequired();
        row.Property(delivery => delivery.Error).HasMaxLength(MaxErrorLength);

        // A subscription's latest deliveries are read by id, which is in time order.
        row.HasIndex(delivery => new { delivery.SubscriptionId, delivery.Id });
    }
}
