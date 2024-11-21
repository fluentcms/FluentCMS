using System.IO;

namespace FluentCMS.Services.MessageHandlers;

public class ContentTypeMessageHandler(IFileService fileService, IFolderService folderService, IContentTypeService contentTypeService, IContentService contentService) : IMessageHandler<SiteTemplate>
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

                    foreach (var contentDictionary in contentTypeTemplate.Contents)
                    {
                        var processedData = new Dictionary<string, object>();
                        foreach (var field in contentTypeTemplate.Fields)
                        {
                            if (contentDictionary.TryGetValue(field.Name, out var value))
                            {
                                if (field.Type == "file" && value is string filePath)
                                {
                                    processedData[field.Name] = await GetFileIdByPath(siteId, filePath, cancellationToken);
                                }
                                else if (field.Type == "multiple-file" && value is IEnumerable<string> filePaths)
                                {
                                    processedData[field.Name] = filePaths.Select(async filePath => {
                                        return await GetFileIdByPath(siteId, filePath, cancellationToken);
                                    });
                                }
                                //  || field.Type == "multiple-file")
                                else
                                {
                                    processedData[field.Name] = value;
                                }
                            }
                        }

                        var content = new Content
                        {
                            SiteId = notification.Payload.Id,
                            TypeId = contentType.Id,
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

    private async Task<Guid?> GetFileIdByPath(Guid siteId, string filePath, CancellationToken cancellationToken = default)
    {
        // Extract the file name
        var fileName = System.IO.Path.GetFileName(filePath);

        // Extract the folder path (excluding the file name)
        var folderPath = filePath[..filePath.LastIndexOf('/')];

        // find folders by url
        var folder = await folderService.GetByPath(siteId, folderPath, cancellationToken);

        // find file by name and folder id
        var file = await fileService.GetByName(folder.Id, fileName, cancellationToken);

        return file?.Id;
    }
}
