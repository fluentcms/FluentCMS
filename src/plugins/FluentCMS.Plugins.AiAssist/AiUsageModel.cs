using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.AiAssist;

// One call that was sent to a provider: who made it, for which action and field, through which provider and model, what
// it used and whether it succeeded. Nothing of the text or the suggestion is kept.
internal sealed class AiUsage
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string PrincipalKind { get; set; } = "";

    public Guid PrincipalId { get; set; }

    public string Action { get; set; } = "";

    public string Type { get; set; } = "";

    public string Field { get; set; } = "";

    public string Provider { get; set; } = "";

    public string Model { get; set; } = "";

    public long PromptTokens { get; set; }

    public long CompletionTokens { get; set; }

    public long DurationMs { get; set; }

    public bool Succeeded { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class AiUsageModel : IPluginModelContributor
{
    public const string UsageTable = "cms_ai_usage";

    public const int KindMaxLength = 32;
    public const int ActionMaxLength = 32;
    public const int NameMaxLength = 64;
    public const int ProviderMaxLength = 64;
    public const int ModelMaxLength = 128;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiUsage>(usage =>
        {
            usage.ToTable(UsageTable);
            usage.HasKey(item => item.Id);
            usage.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            usage.Property(item => item.SiteId).HasColumnName("site_id");
            usage.Property(item => item.PrincipalKind).HasColumnName("principal_kind").HasMaxLength(KindMaxLength).IsRequired();
            usage.Property(item => item.PrincipalId).HasColumnName("principal_id");
            usage.Property(item => item.Action).HasColumnName("action").HasMaxLength(ActionMaxLength).IsRequired();
            usage.Property(item => item.Type).HasColumnName("type").HasMaxLength(NameMaxLength).IsRequired();
            usage.Property(item => item.Field).HasColumnName("field").HasMaxLength(NameMaxLength).IsRequired();
            usage.Property(item => item.Provider).HasColumnName("provider").HasMaxLength(ProviderMaxLength).IsRequired();
            usage.Property(item => item.Model).HasColumnName("model").HasMaxLength(ModelMaxLength).IsRequired();
            usage.Property(item => item.PromptTokens).HasColumnName("prompt_tokens");
            usage.Property(item => item.CompletionTokens).HasColumnName("completion_tokens");
            usage.Property(item => item.DurationMs).HasColumnName("duration_ms");
            usage.Property(item => item.Succeeded).HasColumnName("succeeded");
            usage.Property(item => item.CreatedAt).HasColumnName("created_at");
            usage.HasIndex(item => new { item.SiteId, item.CreatedAt }).HasDatabaseName("ix_cms_ai_usage_site_created_at");
            usage.HasIndex(item => item.CreatedAt).HasDatabaseName("ix_cms_ai_usage_created_at");
        });
    }
}
