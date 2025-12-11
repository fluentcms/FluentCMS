namespace FluentCMS.Api.Core.Events;

public class PageAddedEvent(Page page) : EventBase
{
    public Page Page { get; } = page;
}

public class PageUpdatedEvent(Page page) : EventBase
{
    public Page Page { get; } = page;
}

public class PageRemovedEvent(Page page) : EventBase
{
    public Page Page { get; } = page;
}
