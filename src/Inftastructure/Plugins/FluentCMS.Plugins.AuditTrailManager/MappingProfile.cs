namespace FluentCMS.Plugins.AuditTrailManager;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };

        CreateMap<IApplicationExecutionContext, AuditTrailInternal>().ReverseMap();

        // Map from AuditTrail to AuditTrailInternal
        CreateMap<AuditTrail, AuditTrailInternal>()
            .IncludeMembers(src => src.Context)
            .ForMember(dest => dest.EntityId, opt => opt.MapFrom(src => ((IAuditableEntity)src.Entity).Id))
            .ForMember(dest => dest.EntityVersion, opt => opt.MapFrom(src => ((IAuditableEntity)src.Entity).Version))
            .ForMember(dest => dest.Entity, opt => opt.MapFrom(src => JsonSerializer.Serialize(src.Entity, options)))
            .ForMember(dest => dest.EntityType, opt => opt.MapFrom(src => src.Entity.GetType().FullName));

        // Map from AuditTrailInternal to AuditTrail
        CreateMap<AuditTrailInternal, AuditTrail>()
            .ForMember(dest => dest.Context, opt => opt.MapFrom(src => src))
            .ForMember(dest => dest.Entity, opt => opt.MapFrom((src, dest) => DeserializeEntity(src.Entity, src.EntityType)));
    }

    private static object DeserializeEntity(string? entityJson, string entityType)
    {
        if (!string.IsNullOrEmpty(entityJson) && !string.IsNullOrEmpty(entityType))
        {
            var type = Type.GetType(entityType) ??
                throw new ArgumentException($"Type '{entityType}' not found.");

            // Deserialize to the specific type
            return JsonSerializer.Deserialize(entityJson, type) ??
                throw new JsonException($"Failed to deserialize JSON to type '{entityType}'.");
        }

        throw new ArgumentNullException("Entity JSON or type cannot be null or empty.");
    }
}