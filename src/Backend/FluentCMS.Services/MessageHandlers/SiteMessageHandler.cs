namespace FluentCMS.Services.MessageHandlers;

public class SiteMessageHandler(ISiteService siteService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupInitializeSite:
                await siteService.Create(notification.Payload, cancellationToken);
                break;

            default:
                break;
        }
    }
}
