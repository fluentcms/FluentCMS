using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services.MessageHandlers;

public class ContentTypeMessageHandler(IContentTypeService contentTypeService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                foreach (var contentTypeTemplate in notification.Payload.ContentTypes)
                {
                    var contentType = new ContentType
                    {
                        Id = contentTypeTemplate.Id,
                        Slug = contentTypeTemplate.Slug,
                        Title = contentTypeTemplate.Title,
                        Description = contentTypeTemplate.Description,
                        Fields = contentTypeTemplate.Fields
                    };

                    await contentTypeService.Create(contentType, cancellationToken);
                }

                break;

            default:
                break;
        }
    }
}
