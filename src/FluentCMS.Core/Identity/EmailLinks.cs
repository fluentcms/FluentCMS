namespace FluentCMS.Core.Identity;

// The link of an email: the page's absolute address with the parameters added to any query it has.
internal static class EmailLinks
{
    public static Uri With(Uri page, params (string Name, string Value)[] parameters)
    {
        var builder = new UriBuilder(page);
        var query = builder.Query.TrimStart('?');
        builder.Query = string.Join('&', parameters.Select(parameter => parameter.Name + "=" + Uri.EscapeDataString(parameter.Value)).Prepend(query).Where(part => part.Length > 0));
        return builder.Uri;
    }
}
