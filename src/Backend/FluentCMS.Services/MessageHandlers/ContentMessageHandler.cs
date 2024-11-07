namespace FluentCMS.Services.MessageHandlers;

public class ContentMessageHandler(IContentService contentService, IContentTypeService contentTypeService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var contentType in notification.Payload.ContentTypes)
                {
                    foreach (var contentDictionary in contentType.Contents)
                    {
                        var content = new Content
                        {
                            SiteId = contentType.SiteId,
                            TypeId = contentType.Id,
                            Data = contentDictionary
                        };
                        await contentService.Create(content, cancellationToken);
                    }
                }

                break;

            default:
                break;
        }
    }
}
