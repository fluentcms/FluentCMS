namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request to update host settings.
/// </summary>
public class HostUpdateRequest
{
    /// <summary>
    /// Gets or sets the list of superuser usernames or identifiers.
    /// </summary>
    /// <value>
    /// A list of strings representing the superusers.
    /// </value>
    public List<string> SuperUsers { get; set; } = [];
}
