using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Plugins;

// FieldActionAttribute.FieldsFrom: a path the admin's client takes, then '#' and the member of the answer. The path is resolved
// under a root as the client resolves it, so that what the client refuses, such as file:x, is refused here; a dot segment is
// refused too, plain or encoded, even where it stays under the root.
internal static class FieldActionSource
{
    private static readonly Uri Root = new("https://admin.invalid/api/management/v1/");

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

        if (!Uri.TryCreate(Root, read, out var target) || !Root.IsBaseOf(target)
            || !string.Equals(target.GetLeftPart(UriPartial.Authority), Root.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        (path, member) = (read, value[(hash + 1)..]);
        return true;
    }
}
