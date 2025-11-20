namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Site, SiteDto>();
        CreateMap<SiteAddRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();


        CreateMap<Page, PageDto>();
        CreateMap<PageAddRequest, Page>();
        CreateMap<PageUpdateRequest, Page>();
    }
}
