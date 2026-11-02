namespace FluentCMS.Content;

/// <summary>
/// Validates a whole entry, for rules that span fields or need a service. Register it in the service collection;
/// it runs after every field has been checked, for entries whose CLR type is <typeparamref name="T"/> or derives from it.
/// </summary>
/// <typeparam name="T">The CLR type of the entry.</typeparam>
public interface IEntryValidator<T>
{
    /// <summary>Validates an entry, adding errors to the context.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="context">What the validator knows about the entry; <see cref="ValidationContext.Field"/> is <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <returns>A task that completes when the entry has been checked.</returns>
    ValueTask ValidateAsync(T entry, ValidationContext context, CancellationToken cancellationToken);
}
