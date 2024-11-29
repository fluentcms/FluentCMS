using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Logging.Models;

internal static class HeaderDictionaryExtensions
{
    public static Dictionary<string, string> ToStringDictionary(this IHeaderDictionary dictionary)
    {
        var result = new Dictionary<string, string>();

        foreach (var (key, value) in dictionary)
        {
            result.Add(key, value.ToString());
        }

        return result;
    }
}
