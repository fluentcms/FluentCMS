namespace FluentCMS.Api.Core.Events;

public class SiteAddedEvent(Site site) : EventBase
{
    public Site Site { get; } = site;
}

public class SiteUpdatedEvent(Site site) : EventBase
{
    public Site Site { get; } = site;
}

public class SiteRemovedEvent(Site site) : EventBase
{
    public Site Site { get; } = site;
}
