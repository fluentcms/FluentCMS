using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Infrastructure.Persistence;

public sealed class FluentCmsUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FluentCmsRole : IdentityRole<Guid>;
