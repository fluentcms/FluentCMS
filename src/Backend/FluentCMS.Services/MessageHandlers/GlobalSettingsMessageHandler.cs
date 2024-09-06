using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;

namespace FluentCMS.Services.MessageHandlers;

public class GlobalSettingsMessageHandler(IGlobalSettingsService globalSettingsService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                var globalSettings = new GlobalSettings
                {
                    SuperAdmins = [notification.Payload.Username]
                };
                await globalSettingsService.Update(globalSettings, cancellationToken);
                break;

            default:
                break;
        }
    }
}
