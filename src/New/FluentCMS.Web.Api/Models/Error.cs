namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents an error detail in API responses.
/// </summary>
public class Error
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    /// <value>
    /// A string representing the error code, which categorizes the type of error.
    /// </value>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a brief description of the error.
    /// </summary>
    /// <value>
    /// A string providing a short description of the error.
    /// </value>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets detailed information about the error, if available.
    /// </summary>
    /// <value>
    /// A string containing detailed information about the error, or null if no additional details are provided.
    /// </value>
    public string? Details { get; set; }
}
