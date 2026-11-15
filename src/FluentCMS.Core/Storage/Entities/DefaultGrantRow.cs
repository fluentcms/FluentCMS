namespace FluentCMS.Core.Storage.Entities;

// A row of cms_default_grant: a permission that a permission provider names for a built-in role by default, which this
// instance has applied once. The row stays when an operator takes the grant away, which keeps the default from being
// given again at the next start. DefaultGrantStartupService writes it.
public sealed class DefaultGrantRow
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = "";

    // The plugin whose provider names the default; null for a provider the host registered on the container itself.
    public string? Plugin { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}
