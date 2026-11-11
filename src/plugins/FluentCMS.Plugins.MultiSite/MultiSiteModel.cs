using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.MultiSite;

// A host name of a site. Hostname is the key, since a host name is one site's in the whole instance: lower case, an
// internationalized name as its A-label. A site with hosts has exactly one primary host, which SiteHostService keeps.
internal sealed class SiteHost
{
    public Guid SiteId { get; set; }

    public string Hostname { get; set; } = "";

    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MultiSiteModel : IPluginModelContributor
{
    public const string HostTable = "cms_multi_site_host";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SiteHost>(host =>
        {
            host.ToTable(HostTable);
            host.HasKey(item => item.Hostname);
            host.Property(item => item.SiteId).HasColumnName("site_id");
            host.Property(item => item.Hostname).HasColumnName("hostname").HasMaxLength(HostNames.MaxLength);
            host.Property(item => item.IsPrimary).HasColumnName("is_primary");
            host.Property(item => item.CreatedAt).HasColumnName("created_at");
            host.HasIndex(item => item.SiteId).HasDatabaseName("ix_cms_multi_site_host_site");
        });
    }
}
