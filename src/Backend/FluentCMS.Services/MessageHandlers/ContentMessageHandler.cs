namespace FluentCMS.Services.MessageHandlers;

public class ContentMessageHandler(IContentService contentService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                foreach (var contentType in notification.Payload.ContentTypes)
                {
                    foreach (var contentDictionary in contentType.Contents)
                    {
                        var content = new Content
                        {
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
