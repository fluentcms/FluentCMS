using System.Net.Http.Headers;

namespace FluentCMS.Web.UI.ApiClients;

public abstract class BaseClient
{
    protected readonly HttpClient HttpClient;
    protected abstract string ControllerName { get; } 
    protected string GetUrl(string action) => $"{ControllerName}/{action}";

    protected BaseClient(IHttpClientFactory httpClientFactory)
    {
        HttpClient = httpClientFactory.CreateClient("FluentCMS.Web.UI");
        HttpClient.BaseAddress = new Uri("https://localhost:7164/api/");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
