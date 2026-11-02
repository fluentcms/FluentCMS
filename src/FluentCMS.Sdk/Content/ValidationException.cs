namespace FluentCMS.Content;

/// <summary>
/// Thrown when an entry breaks validation rules. The HTTP layer maps it to a 422 problem details response
/// whose <c>errors</c> array holds <c>{ path, code, message }</c> for each error.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="errors">The errors; there must be at least one.</param>
    public ValidationException(IEnumerable<ValidationError> errors)
        : base(BuildMessage(errors, out var list))
    {
        Errors = list;
    }

    /// <summary>The errors, in the order they were found.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    private static string BuildMessage(IEnumerable<ValidationError> errors, out IReadOnlyList<ValidationError> list)
    {
        ArgumentNullException.ThrowIfNull(errors);
        list = [.. errors];
        if (list.Count == 0)
        {
            throw new ArgumentException("A validation exception needs at least one error.", nameof(errors));
        }

        return list.Count == 1
            ? $"Validation failed: {Describe(list[0])}"
            : $"Validation failed with {list.Count} errors: {string.Join("; ", list.Select(Describe))}";
    }

    private static string Describe(ValidationError error) =>
        error.Path.Length == 0 ? $"{error.Code} {error.Message}" : $"{error.Path}: {error.Code} {error.Message}";
}
