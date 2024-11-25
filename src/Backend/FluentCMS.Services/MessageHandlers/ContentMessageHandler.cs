namespace FluentCMS.Services.MessageHandlers;

public class ContentMessageHandler(IContentService contentService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteFilesCreated:
                var site = notification.Payload;
                foreach (var contentTypeTemplate in site.ContentTypes)
                {
                    foreach (var contentDictionary in contentTypeTemplate.Contents)
                    {
                        var processedData = new Dictionary<string, object?>();
                        foreach (var field in contentTypeTemplate.Fields)
                        {
                            if (contentDictionary.TryGetValue(field.Name, out var value))
                            {
                                if (field.Type == "single-file" && value is string filePath)
                                {
                                    processedData[field.Name] = GetFileIdByPath(site, filePath, cancellationToken);
                                }
                                else if (field.Type == "multiple-file" && value is IEnumerable<string> filePaths)
                                {
                                    processedData[field.Name] = filePaths.Select(filePath => {
                                        return GetFileIdByPath(site, filePath, cancellationToken);
                                    });
                                }
                                else
                                {
                                    processedData[field.Name] = value;
                                }
                            }
                        }

                        var content = new Content
                        {
                            SiteId = site.Id,
                            TypeId = contentTypeTemplate.Id,
                            Data = processedData
                        };
                        await contentService.Create(content, cancellationToken);
                    }
                }
                break;

            default:
                break;
        }
    }

    private static Guid? GetFileIdByPath(SiteTemplate siteTemplate, string filePath, CancellationToken cancellationToken = default)
    {
        var file = siteTemplate.Files.FirstOrDefault(x => x.Path == filePath);
        return file?.Id;
    }
}
