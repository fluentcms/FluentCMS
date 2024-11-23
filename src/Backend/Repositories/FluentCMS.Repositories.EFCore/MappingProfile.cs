using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        // Generic mappings for base entities
        CreateMap<EntityModel, Entity>().ReverseMap();

        // CreatedBy and CreatedAt should not be mapped
        CreateMap<AuditableEntityModel, AuditableEntity>().ReverseMap().
            ForMember(dest => dest.CreatedBy, opt => opt.Ignore()).
            ForMember(dest => dest.CreatedAt, opt => opt.Ignore());

        CreateMap<SiteAssociatedEntityModel, SiteAssociatedEntity>().ReverseMap();

        // Map between ApiTokenModel and ApiToken
        CreateMap<ApiTokenModel, ApiToken>().ReverseMap();

        // Map between ApiTokenPolicyModel and Policy
        CreateMap<ApiTokenPolicyModel, Policy>()
            .ForMember(dest => dest.Actions, opt => opt.MapFrom(src =>
                src.Actions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())) // Convert comma-separated string to List<string>
            .ReverseMap()
            .ForMember(dest => dest.Actions, opt => opt.MapFrom(src =>
                string.Join(',', src.Actions))); // Convert List<string> to comma-separated string

        CreateMap<BlockModel, Block>().ReverseMap();

        // Map between ContentModel and Content
        CreateMap<ContentModel, Content>()
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => src.Data.ToDictionary(
                data => data.Key,
                data => JsonSerializer.Deserialize<object?>(data.Value, jsonSerializerOptions))))
            .ReverseMap()
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => src.Data.Select(kv => new ContentDataModel
            {
                Key = kv.Key,
                Value = JsonSerializer.Serialize(kv.Value, jsonSerializerOptions)
            })));

        // Map between ContentDataModel and KeyValuePair<string, object?>
        CreateMap<ContentDataModel, KeyValuePair<string, object?>>()
            .ConstructUsing(src => new KeyValuePair<string, object?>(
                src.Key,
                JsonSerializer.Deserialize<object?>(src.Value, jsonSerializerOptions)))
            .ReverseMap()
            .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.Key))
            .ForMember(dest => dest.Value, opt => opt.MapFrom(src => JsonSerializer.Serialize(src.Value, jsonSerializerOptions)));


        // Map between ContentTypeModel and ContentType
        CreateMap<ContentTypeModel, ContentType>()
            .ForMember(dest => dest.Fields, opt => opt.MapFrom(src => src.Fields)) // Map Fields
            .ReverseMap()
            .ForMember(dest => dest.Fields, opt => opt.MapFrom(src => src.Fields));

        // Map between ContentTypeFieldModel and ContentTypeField
        CreateMap<ContentTypeFieldModel, ContentTypeField>()
            .ForMember(dest => dest.Settings, opt => opt.MapFrom(src => src.Settings.ToDictionary(
                setting => setting.Key,
                setting => JsonSerializer.Deserialize<object?>(setting.Value, jsonSerializerOptions))))
            .ReverseMap()
            .ForMember(dest => dest.Settings, opt => opt.MapFrom(src =>
                src.Settings != null ?
                src.Settings.Select(kv => new ContentTypeFieldSettingsModel
                {
                    Key = kv.Key,
                    Value = JsonSerializer.Serialize(kv.Value, jsonSerializerOptions)
                }) : new List<ContentTypeFieldSettingsModel>()
                ));

        // Map between ContentTypeFieldSettingsModel and KeyValuePair<string, object?>
        CreateMap<ContentTypeFieldSettingsModel, KeyValuePair<string, object?>>()
            .ConstructUsing(src => new KeyValuePair<string, object?>(
                src.Key,
                JsonSerializer.Deserialize<object?>(src.Value, jsonSerializerOptions)))
            .ReverseMap()
            .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.Key))
            .ForMember(dest => dest.Value, opt => opt.MapFrom(src => JsonSerializer.Serialize(src.Value, jsonSerializerOptions)));

        CreateMap<FileModel, File>().ReverseMap();
        CreateMap<FolderModel, Folder>().ReverseMap();

        CreateMap<GlobalSettingsModel, GlobalSettings>()
            .ForMember(dest => dest.SuperAdmins, opt => opt.MapFrom(src => src.SuperAdmins.Split(';', StringSplitOptions.RemoveEmptyEntries)))
            .ReverseMap()
            .ForMember(dest => dest.SuperAdmins, opt => opt.MapFrom(src => string.Join(';', src.SuperAdmins)));

        CreateMap<LayoutModel, Layout>().ReverseMap();

        CreateMap<PageModel, Page>().ReverseMap();

        CreateMap<PermissionModel, Permission>().ReverseMap();

        // Map between PluginDefinitionModel and PluginDefinition
        CreateMap<PluginDefinitionModel, PluginDefinition>().ReverseMap();

        // Map between PluginDefinitionTypeModel and PluginDefinitionType
        CreateMap<PluginDefinitionTypeModel, PluginDefinitionType>()
            .ReverseMap();

        // Map between PluginContentDataModel and KeyValuePair<string, object?>
        CreateMap<PluginContentDataModel, KeyValuePair<string, object?>>()
            .ConstructUsing(src => new KeyValuePair<string, object?>(
                src.Key,
                JsonSerializer.Deserialize<object?>(src.Value, jsonSerializerOptions)))
            .ReverseMap()
            .ForMember(dest => dest.Key, opt => opt.MapFrom(src => src.Key))
            .ForMember(dest => dest.Value, opt => opt.MapFrom(src => JsonSerializer.Serialize(src.Value, jsonSerializerOptions)));

        // Map between PluginContentModel and PluginContent
        CreateMap<PluginContentModel, Dictionary<string, object?>>()
            .ConvertUsing((src, dest) =>
                src.Data?.ToDictionary(
                    data => data.Key,
                    data => JsonSerializer.Deserialize<object?>(data.Value, jsonSerializerOptions)) ?? []);

        CreateMap<PluginModel, Plugin>().ReverseMap();

        CreateMap<RoleModel, Role>().ReverseMap();

        // Map between SettingsModel and Settings
        CreateMap<SettingsModel, Settings>()
            .ForMember(dest => dest.Values, opt => opt.MapFrom(src =>
                src.Values.ToDictionary(
                    setting => setting.Key,
                    setting => setting.Value)))
            .ReverseMap()
            .ForMember(dest => dest.Values, opt => opt.MapFrom(src =>
                src.Values.Select(kv => new SettingValuesModel
                {
                    Key = kv.Key,
                    Value = kv.Value
                })));

    }
}
