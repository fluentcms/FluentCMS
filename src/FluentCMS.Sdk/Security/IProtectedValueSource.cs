namespace FluentCMS.Security;

/// <summary>
/// Names the values a plugin stores protected with Data Protection, so that <c>fluentcms keyring reprotect</c> protects them
/// again under the current default key of the key ring before an older ring key or certificate is retired. Register an
/// implementation in the service collection, scoped or as a singleton. The command reads the values, protects again each
/// one that is under another key, and writes it back through <see cref="WriteAsync"/>; it never shows a value.
/// </summary>
public interface IProtectedValueSource
{
    /// <summary>
    /// Reads the protected values as they are stored now. A value that is no longer needed, such as a replaced secret whose
    /// overlap has ended, may be left out, and so may one stored before it was protected.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The values, or none.</returns>
    Task<IReadOnlyList<ProtectedValue>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a value with the same value protected again, only while the store still holds what was read, and changes
    /// nothing else of the row.
    /// </summary>
    /// <param name="value">The value as <see cref="ReadAsync"/> read it.</param>
    /// <param name="reprotected">What to store instead, in the form of <see cref="ProtectedValue.Stored"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> when it was written; <see langword="false"/> when the stored value changed or is gone since it
    /// was read, which the command then reads again.
    /// </returns>
    Task<bool> WriteAsync(ProtectedValue value, string reprotected, CancellationToken cancellationToken);
}
