using System.Text.Json;

namespace FluentCMS.Repositories.EFCore;

public class MappingProfile : Profile
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public MappingProfile()
    {
        _jsonSerializerOptions = new JsonSerializerOptions();
        _jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

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
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => MapStringToDictionary(src.Data)))
            .ReverseMap()
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => MapDictionaryToString(src.Data)));

        // Map between ContentTypeModel and ContentType
        CreateMap<ContentTypeModel, ContentType>().ReverseMap();

        // Map between ContentTypeFieldModel and ContentTypeField
        CreateMap<ContentTypeFieldModel, ContentTypeField>()
            .ForMember(dest => dest.Settings, opt => opt.MapFrom(src => MapStringToDictionary(src.Settings)))
            .ReverseMap()
            .ForMember(dest => dest.Settings, opt => opt.MapFrom(src => MapDictionaryToString(src.Settings)));

        CreateMap<FileModel, File>().ReverseMap();
        CreateMap<FolderModel, Folder>().ReverseMap();

        CreateMap<GlobalSettingsModel, GlobalSettings>()
            .ForMember(dest => dest.SuperAdmins, opt => opt.MapFrom(src => src.SuperAdmins.Split(';', StringSplitOptions.RemoveEmptyEntries)))
            .ReverseMap()
            .ForMember(dest => dest.SuperAdmins, opt => opt.MapFrom(src => string.Join(';', src.SuperAdmins)));

        CreateMap<LayoutModel, Layout>().ReverseMap();

        CreateMap<PageModel, Page>().ReverseMap();

        CreateMap<PermissionModel, Permission>().ReverseMap();

          // Map from PluginDefinitionModel to PluginDefinition
        CreateMap<PluginDefinitionModel, PluginDefinition>()
            .ForMember(dest => dest.Stylesheets, opt => opt.MapFrom(src =>
                string.IsNullOrWhiteSpace(src.Stylesheets)
                    ? new List<string>()
                    : src.Stylesheets.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()));

        // Map from PluginDefinition to PluginDefinitionModel
        CreateMap<PluginDefinition, PluginDefinitionModel>()
            .ForMember(dest => dest.Stylesheets, opt => opt.MapFrom(src =>
                src.Stylesheets == null || !src.Stylesheets.Any()
                    ? string.Empty
                    : string.Join(',', src.Stylesheets)));

        // Map between PluginDefinitionTypeModel and PluginDefinitionType
        CreateMap<PluginDefinitionTypeModel, PluginDefinitionType>().ReverseMap();

        // Map from PluginContentModel to PluginContent
        CreateMap<PluginContentModel, PluginContent>()
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => MapStringToDictionary(src.Data)))
            .ReverseMap()
            .ForMember(dest => dest.Data, opt => opt.MapFrom(src => MapDictionaryToString(src.Data)));

        CreateMap<PluginModel, Plugin>().ReverseMap();

        CreateMap<RoleModel, Role>().ReverseMap();

        // Map from SiteModel to Site
        CreateMap<SiteModel, Site>()
            .ForMember(dest => dest.Urls, opt => opt.MapFrom(src =>
                string.IsNullOrWhiteSpace(src.Urls)
                    ? new List<string>()
                    : src.Urls.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()));

        // Map from Site to SiteModel
        CreateMap<Site, SiteModel>()
            .ForMember(dest => dest.Urls, opt => opt.MapFrom(src =>
                src.Urls == null || !src.Urls.Any()
                    ? string.Empty
                    : string.Join(',', src.Urls)));

        CreateMap<UserRoleModel, UserRole>().ReverseMap();

        CreateMap<UserModel, User>().ReverseMap();

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

    // mapp Dictionary<string, object?> to string
    private string MapDictionaryToString(Dictionary<string, object?>? dictionary)
    {
        if (dictionary == null)
            return string.Empty;

        return JsonSerializer.Serialize(dictionary, _jsonSerializerOptions);
    }

    // mapp string to Dictionary<string, object?>
    private Dictionary<string, object?> MapStringToDictionary(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(value, _jsonSerializerOptions) ?? [];
    }
}
