namespace FluentCMS.Admin;

/// <summary>
/// Announces a message to assistive technology through the live regions of the shell: a save, a publish, the progress of a
/// bulk action or an error. The message is read out and not shown, so a component still shows its result where it happened.
/// </summary>
public interface IAnnouncer
{
    /// <summary>Announces a message.</summary>
    /// <param name="message">The text to read out, from the resources of the calling assembly.</param>
    /// <param name="politeness">
    /// <see cref="AnnouncementPoliteness.Polite"/> for a result that can wait until the user pauses, such as a save;
    /// <see cref="AnnouncementPoliteness.Assertive"/> for one that interrupts, such as an error.
    /// </param>
    void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite);
}
