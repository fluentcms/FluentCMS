using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;

namespace FluentCMS.Services.MessageHandlers;

public class LayoutMessageHandler(ILayoutService layoutService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupInitializeLayouts:
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
}
