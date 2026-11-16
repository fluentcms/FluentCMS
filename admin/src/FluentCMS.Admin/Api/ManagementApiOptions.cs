namespace FluentCMS.Admin.Api;

// Where the Management API is. The runtime configuration sets ApiBaseUrl from config.json's apiBaseUrl (spec 2.5).
public sealed class ManagementApiOptions
{
    internal const string ManagementPath = "api/management/v1/";

    // The origin and path base of the API host, such as https://cms.example.com or https://example.com/cms: absolute http or
    // https, without a query or a fragment.
    public Uri? ApiBaseUrl { get; set; }

    // The handler under the pipeline: the browser's fetch when null, the test server's in the tests.
    internal Func<HttpMessageHandler>? PrimaryHandler { get; set; }

    internal Uri ManagementAddress()
    {
        if (ApiBaseUrl is not { IsAbsoluteUri: true } baseUrl
            || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp)
            || baseUrl.Query.Length > 0
            || baseUrl.Fragment.Length > 0)
        {
            throw new InvalidOperationException($"{nameof(ManagementApiOptions)}.{nameof(ApiBaseUrl)} is not an absolute http or https URL without a query or a fragment.");
        }

        var withSlash = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        return new Uri(withSlash, ManagementPath);
    }
}
