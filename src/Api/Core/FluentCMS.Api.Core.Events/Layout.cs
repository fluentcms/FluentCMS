namespace FluentCMS.Api.Core.Events;

public class LayoutAddedEvent(Layout layout) : EventBase
{
    public Layout Layout { get; } = layout;
}

public class LayoutUpdatedEvent(Layout layout) : EventBase
{
    public Layout Layout { get; } = layout;
}

public class LayoutRemovedEvent(Layout layout) : EventBase
{
    public Layout Layout { get; } = layout;
}

public class LayoutsRemovedBySiteEvent(IEnumerable<Layout> layouts) : EventBase
{
    public IEnumerable<Layout> Layout { get; } = layouts;
}
