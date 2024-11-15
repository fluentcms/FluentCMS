using AutoMapper;

namespace FluentCMS.Repositories.EFCore;

public class MappingProfile : Profile
{
    public MappingProfile()
    {

        #region ApiToken

        // Map from DbModels.ApiToken to Entities.ApiToken
        CreateMap<DbModels.ApiToken, ApiToken>()
            .ForMember(dest => dest.Policies, opt => opt.MapFrom(src => src.Policies));

        // Map from DbModels.ApiTokenPolicy to Entities.Policy
        CreateMap<DbModels.ApiTokenPolicy, Policy>()
            .ForMember(dest => dest.Actions, opt => opt.MapFrom(src => src.Actions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()));

        // Map from Entities.ApiToken to DbModels.ApiToken
        CreateMap<ApiToken, DbModels.ApiToken>()
            .ForMember(dest => dest.Policies, opt => opt.MapFrom(src => src.Policies))
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore());

        // Map from Entities.Policy to DbModels.ApiTokenPolicy
        CreateMap<Policy, DbModels.ApiTokenPolicy>()
            .ForMember(dest => dest.Actions,
                       opt => opt.MapFrom(src => string.Join(",", src.Actions)));

        #endregion

    }
}
