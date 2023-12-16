namespace FluentCMS.Entities;

/// <summary>
/// Represents a host entity.
/// Inherits from <see cref="AuditEntity" /> for audit tracking.
/// </summary>
public class Host : AuditEntity
{
    /// <summary>
    /// List of super users associated with the host.
    /// </summary>
    public List<string> SuperUsers { get; set; } = [];
}
