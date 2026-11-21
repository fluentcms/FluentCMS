using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Plugins;

// FieldActionAttribute.FieldsFrom: a path the admin's client takes, then '#' and the member of the answer. A dot segment is
// refused too, plain or encoded, since the client refuses a path that leads out of the Management API.
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

        var read = value[..hash];
        if (read.Split('?', 2)[0].Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or ".."))
        {
            return false;
        }

        try
        {
            _ = new ApiRequest(HttpMethod.Get, read);
        }
        catch (ArgumentException)
        {
            return false;
        }

        (path, member) = (read, value[(hash + 1)..]);
        return true;
    }
}
