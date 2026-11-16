namespace FluentCMS.Admin.Accessibility;

// Hands announcements to the live regions of the shell, which LiveRegions renders.
internal sealed class Announcer : IAnnouncer
{
    public event Action<string, AnnouncementPoliteness>? Announced;

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Announced?.Invoke(message, politeness);
    }
}
