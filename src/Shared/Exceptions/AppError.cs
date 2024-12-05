namespace FluentCMS;

/// <summary>
/// Represents an error associated with an API call.
/// </summary>
public class AppError
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the error.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppError"/> class.
    /// </summary>
    public AppError()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppError"/> class with the specified error code.
    /// </summary>
    /// <param name="code">The error code.</param>
    public AppError(string code)
    {
        Code = code;
    }

    /// <summary>
    /// Returns a string representation of the <see cref="AppError"/>.
    /// </summary>
    /// <returns>A string containing the error code and description.</returns>
    public override string ToString()
    {
        return $"{Code}-{Description}";
    }
}
