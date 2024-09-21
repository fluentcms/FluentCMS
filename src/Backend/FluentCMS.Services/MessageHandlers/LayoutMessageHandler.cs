namespace FluentCMS.Services.MessageHandlers;

public class LayoutMessageHandler(ILayoutService layoutService) : IMessageHandler<SiteTemplate>, IMessageHandler<Site>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var layout in notification.Payload.Layouts)
                {
                    layout.SiteId = notification.Payload.Id;
                    await layoutService.Create(layout, cancellationToken);
                }

                break;

            default:
                break;
        }
    }

    public async Task Handle(Message<Site> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteDeleted:
                await layoutService.DeleteBySiteId(notification.Payload.Id, cancellationToken);
                break;

            default:
                break;
        }
    }
}
