namespace FluentCMS.Content;

/// <summary>The outcome of validating an entry.</summary>
public sealed class ValidationResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="errors">The errors, in the order they were found.</param>
    public ValidationResult(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = [.. errors];
    }

    /// <summary>The result of a valid entry.</summary>
    public static ValidationResult Success { get; } = new([]);

    /// <summary>Whether no rule was broken.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>The errors, in the order they were found: fields in declaration order, then custom field validators, then entry validators.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }
}
