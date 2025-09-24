using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Providers.Repositories;

/// <summary>
/// Represents a provider instance stored in the database.
/// </summary>
public class Provider : AuditableEntity
{
    /// <summary>
    /// Unique name for the provider within its area.
    /// </summary>
    [Required(ErrorMessage = "Provider name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Provider name must be between 1 and 100 characters")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The display name of this provider for administrative purposes.
    /// </summary>
    [Required(ErrorMessage = "Display name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Display name must be between 1 and 200 characters")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The functional area this provider belongs to (e.g., "Email", "VirtualFile").
    /// </summary>
    [Required(ErrorMessage = "Area is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Area must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9]*$", ErrorMessage = "Area must start with a letter and contain only alphanumeric characters")]
    public string Area { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the provider's module class.
    /// </summary>
    [Required(ErrorMessage = "Module type is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Module type must be between 1 and 200 characters")]
    public string ModuleType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this provider is currently active.
    /// Only one provider per area can be active at a time.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// JSON string for the provider options.
    /// </summary>
    public string? Options { get; set; }
}
