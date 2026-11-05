namespace FluentCMS.Providers;

/// <summary>
/// Sends email, such as invitations to the admin. Core registers a sender that only writes the message to the log in
/// Development and refuses to send anywhere else; a host or a plugin replaces it with a real one.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends a message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the message is handed over for delivery.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
