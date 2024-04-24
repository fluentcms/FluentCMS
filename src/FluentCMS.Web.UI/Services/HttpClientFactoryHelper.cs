using System.Net.Http.Headers;

namespace FluentCMS.Web.UI.Services;

public static class HttpClientFactoryHelper
{
    public const string HTTP_CLIENT_API_NAME = "FluentCMS.Web.Api";

    public static HttpClient CreateApiClient(this IHttpClientFactory httpClientFactory)
    {
        return httpClientFactory.CreateClient(HTTP_CLIENT_API_NAME);
    }

    public static TClient CreateApiClient<TClient>(this IHttpClientFactory httpClientFactory, UserLoginResponse? userLogin) where TClient : class, IApiClient
    {
        var httpClient = httpClientFactory.CreateApiClient();

        if (!string.IsNullOrEmpty(userLogin?.Token))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLogin.Token);

        var ctor = typeof(TClient).GetConstructor([typeof(HttpClient)]) ??
                    throw new InvalidOperationException($"Could not find constructor for {typeof(TClient).Name}");

        return (TClient)ctor.Invoke([httpClient]);
    }
}
