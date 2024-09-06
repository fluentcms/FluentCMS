using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;

namespace FluentCMS.Services.MessageHandlers;

public class SiteMessageHandler(ISiteService siteService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupInitializeSite:
                var layouts = notification.Payload.Layouts;
                var site = new Site
                {
                    Id = notification.Payload.Id,
                    Name = notification.Payload.Name,
                    Description = notification.Payload.Description,
                    Urls = [notification.Payload.Url],
                    LayoutId = layouts.Where(x => x.Name == notification.Payload.Layout).Single().Id,
                    EditLayoutId = layouts.Where(x => x.Name == notification.Payload.EditLayout).Single().Id,
                    DetailLayoutId = layouts.Where(x => x.Name == notification.Payload.DetailLayout).Single().Id
                };
                await siteService.Create(site, cancellationToken);
                break;

            default:
                break;
        }

    }
}
