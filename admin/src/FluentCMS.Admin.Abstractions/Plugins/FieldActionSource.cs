using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Plugins;

// FieldActionAttribute.FieldsFrom: a path the admin's client takes, then '#' and the member of the answer.
internal static class FieldActionSource
{
    public static bool TryParse(string? value, out string path, out string member)
    {
        (path, member) = ("", "");
        var hash = value?.LastIndexOf('#') ?? -1;
        if (value is null || hash <= 0 || hash == value.Length - 1)
        {
            return false;
        }

        try
        {
            _ = new ApiRequest(HttpMethod.Get, value[..hash]);
        }
        catch (ArgumentException)
        {
            return false;
        }

        (path, member) = (value[..hash], value[(hash + 1)..]);
        return true;
    }
}
