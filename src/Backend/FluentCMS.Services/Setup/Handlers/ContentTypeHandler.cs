namespace FluentCMS.Services.Setup.Handlers;

public class ContentTypeHandler(IContentTypeService contentTypeService, IContentService contentService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.ContentType;

    public override async Task<SetupContext> Handle(SetupContext context)
    {
        foreach (var contentTypeTemplate in context.AdminTemplate.ContentTypes)
        {
            var contentType = new ContentType
            {
                Slug = contentTypeTemplate.Slug,
                Title = contentTypeTemplate.Title,
                Description = contentTypeTemplate.Description,
                Fields = contentTypeTemplate.Fields
            };

            await contentTypeService.Create(contentType);
            foreach (var contentDictionary in contentTypeTemplate.Contents)
            {
                var content = new Content
                {
                    TypeId = contentType.Id,
                    Data = contentDictionary
                };
                await contentService.Create(content);
            }
        }

        return await base.Handle(context);
    }
}
