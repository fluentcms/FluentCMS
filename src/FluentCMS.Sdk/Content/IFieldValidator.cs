namespace FluentCMS.Content;

/// <summary>
/// Validates the value of a field. Register it in the service collection; it runs for every field whose
/// CLR type is <typeparamref name="TField"/> (use <see cref="object"/> to see every field) after the declarative
/// rules, and is skipped for a hidden field. A <see langword="null"/> value reaches only validators of reference types.
/// </summary>
/// <typeparam name="TField">The CLR type of the field value.</typeparam>
public interface IFieldValidator<TField>
{
    /// <summary>Validates a value, adding errors to the context.</summary>
    /// <param name="value">The value of the field.</param>
    /// <param name="context">What the validator knows about the entry, with <see cref="ValidationContext.Field"/> set.</param>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <returns>A task that completes when the value has been checked.</returns>
    ValueTask ValidateAsync(TField? value, ValidationContext context, CancellationToken cancellationToken);
}
