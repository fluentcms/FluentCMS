namespace FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Site, SiteDto>();
        CreateMap<SiteAddRequest, Site>();
        CreateMap<SiteUpdateRequest, Site>();
    }
}
