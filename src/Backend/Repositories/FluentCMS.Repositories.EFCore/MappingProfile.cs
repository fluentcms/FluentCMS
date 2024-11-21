using AutoMapper;
using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

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

        #region ContentType

        // Map from DbModels.ContentType to Entities.ContentType
        CreateMap<DbModels.ContentType, ContentType>()
            .ForMember(dest => dest.Fields, opt => opt.MapFrom(src => src.Fields.ToList()));

        // Map from Entities.ContentType to DbModels.ContentType
        CreateMap<ContentType, DbModels.ContentType>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore());

        // Map from DbModels.ContentTypeField to Entities.ContentTypeField
        CreateMap<DbModels.ContentTypeField, ContentTypeField>();

        // Map from Entities.ContentTypeField to DbModels.ContentTypeField
        CreateMap<ContentTypeField, DbModels.ContentTypeField>();

        #endregion

        #region Settings

        // Map from DbModels.Settings to Entities.Settings
        CreateMap<DbModels.Settings, Settings>()
            .ForMember(dest => dest.Values, opt => opt.MapFrom(src =>
                src.Values.ToDictionary(v => v.Key, v => v.Value)));

        // Map from Entities.Settings to DbModels.Settings
        CreateMap<Settings, DbModels.Settings>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.Values, opt => opt.MapFrom(src =>
                src.Values.Select(kv => new DbModels.SettingValue
                {
                    Key = kv.Key,
                    Value = kv.Value
                }).ToList()));

        // Map from DbModels.SettingValue to a dictionary entry (not directly used, but for completeness)
        CreateMap<DbModels.SettingValue, KeyValuePair<string, string>>()
            .ConstructUsing(src => new KeyValuePair<string, string>(src.Key, src.Value));

        // Map from a dictionary entry to DbModels.SettingValue (not directly used, but for completeness)
        CreateMap<KeyValuePair<string, string>, DbModels.SettingValue>()
            .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.Key))
            .ForMember(dest => dest.Value, opt => opt.MapFrom(src => src.Value));

        #endregion
    }
}
