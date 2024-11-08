namespace FluentCMS.Services.MessageHandlers;

public class ContentTypeMessageHandler(IContentTypeService contentTypeService, IContentService contentService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var contentTypeTemplate in notification.Payload.ContentTypes)
                {
                    var contentType = new ContentType
                    {

                        SiteId = notification.Payload.Id,
                        Slug = contentTypeTemplate.Slug,
                        Title = contentTypeTemplate.Title,
                        Description = contentTypeTemplate.Description,
                        Fields = contentTypeTemplate.Fields
                    };

                    contentType = await contentTypeService.Create(contentType, cancellationToken);

                    foreach (var contentDictionary in contentTypeTemplate.Contents)
                    {
                        var content = new Content
                        {
                            SiteId = notification.Payload.Id,
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
