namespace FluentCMS.Services.MessageHandlers;

public class ContentTypeMessageHandler(IContentTypeService contentTypeService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                var siteId = notification.Payload.Id;
                foreach (var contentTypeTemplate in notification.Payload.ContentTypes)
                {
                    var contentType = new ContentType
                    {
                        SiteId = siteId,
                        Slug = contentTypeTemplate.Slug,
                        Title = contentTypeTemplate.Title,
                        Description = contentTypeTemplate.Description,
                        Fields = contentTypeTemplate.Fields
                    };

                    contentType = await contentTypeService.Create(contentType, cancellationToken);

                    contentTypeTemplate.Id = contentType.Id;
                }
                break;

            default:
                break;
        }
    }
}
