namespace FluentCMS.Web.UI.Services;

public static class HttpClientFactoryHelper
{
    public static T GetClient<T>(this IHttpClientFactory httpClientFactory) where T : IApiClient
    {
        var client = httpClientFactory.CreateClient("FluentCMS.Web.Api");

        var type = typeof(T);

        var ctor = type.GetConstructor([typeof(HttpClient)]) ??
            throw new InvalidOperationException($"Could not find constructor for {type.Name}");

        var instance = ctor.Invoke([client]);

        return (T)instance;
    }
}
