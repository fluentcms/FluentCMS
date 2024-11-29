namespace FluentCMS.Services.MessageHandlers;

public class SettingsMessageHandler(ISettingsService settingsService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                var siteTemplate = notification.Payload;
                if (siteTemplate.Settings != null && siteTemplate.Settings.Count > 0)
                    await settingsService.Update(siteTemplate.Id, siteTemplate.Settings, cancellationToken);
                break;

            default:
                break;
        }
    }
}
