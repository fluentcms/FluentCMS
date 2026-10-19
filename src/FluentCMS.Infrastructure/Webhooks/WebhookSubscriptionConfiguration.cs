using FluentCMS.Core.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Webhooks;

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscriptionRow>
{
    public void Configure(EntityTypeBuilder<WebhookSubscriptionRow> row)
    {
        row.ToTable("WebhookSubscriptions");

        row.HasKey(subscription => subscription.Id);
        row.Property(subscription => subscription.Id).ValueGeneratedNever();
        row.Property(subscription => subscription.Url).HasMaxLength(WebhookSubscription.MaxUrlLength).IsRequired();
        row.Property(subscription => subscription.ProtectedSecret).IsRequired();
        row.Property(subscription => subscription.AddedByName).HasMaxLength(256).IsRequired();
        row.PrimitiveCollection(subscription => subscription.Events);
    }
}
