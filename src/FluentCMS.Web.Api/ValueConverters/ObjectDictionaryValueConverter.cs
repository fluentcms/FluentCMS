using System.Text.Json;

namespace FluentCMS.Web.Api.ValueConverters;

public class ObjectDictionaryValueConverter :
    IValueConverter<Dictionary<string, object?>?, Dictionary<string, object?>?>
{
    public Dictionary<string, object?> Convert(Dictionary<string, object?>? sourceMember, ResolutionContext context)
    {
        if (sourceMember == null)
        {
            return new Dictionary<string, object?>();
        }
        return sourceMember.Select(x => (x.Key, Value: x.Value.MapValue()))
            .ToDictionary(x => x.Key, x => x.Value);
    }

}



public class ObjectValueConverter :
    IValueConverter<object?, object?>
{

    public object? Convert(object? sourceMember, ResolutionContext context)
    {
        return sourceMember.MapValue();
    }

}
