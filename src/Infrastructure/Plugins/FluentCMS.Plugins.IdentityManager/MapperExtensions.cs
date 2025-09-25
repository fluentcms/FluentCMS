namespace FluentCMS.Plugins.IdentityManager;

public static class MapperExtensions
{
    public static ApiResult<TDest> ToApiResult<TDest>(this IMapper mapper, object value)
    {
        var result = mapper.Map<TDest>(value);
        return new ApiResult<TDest>(result);
    }

    public static ApiPagedResult<TDest> ToPagedApiResult<TDest>(this IMapper mapper, IEnumerable<object> values)
    {
        var result = mapper.Map<IEnumerable<TDest>>(values);
        return new ApiPagedResult<TDest>(result);
    }
}
