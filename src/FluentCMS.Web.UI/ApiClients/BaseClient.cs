using System.Collections.Specialized;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;

namespace FluentCMS.Web.UI.ApiClients;

public abstract class BaseClient
{
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    protected readonly HttpClient HttpClient;
    protected abstract string ControllerName { get; }
    protected string GetUrl(string action) => $"{ControllerName}/{action}";

    protected BaseClient(IHttpClientFactory httpClientFactory)
    {
        HttpClient = httpClientFactory.CreateClient("FluentCMS.Web.UI");
        // TODO: Move this to configuration
        HttpClient.BaseAddress = new Uri("https://localhost:7164/api/");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    protected async Task<T> Get<T>(string actionName, NameValueCollection query, CancellationToken cancellationToken = default)
    {
        CaptureExceptionSource();
        var url = MethodName; //GetUrl(actionName);

        var response = await HttpClient.GetFromJsonAsync<T>($"{url}?{query}", cancellationToken: cancellationToken);
        return response == null ? throw new AppApiClientException() : response;
    }

    protected async Task<T> Get<T>(string actionName, CancellationToken cancellationToken = default)
    {
        CaptureExceptionSource();
        var url = MethodName; //GetUrl(actionName);

        var response = await HttpClient.GetFromJsonAsync<T>($"{url}", cancellationToken: cancellationToken);
        return response == null ? throw new AppApiClientException() : response;
    }

    private void CaptureExceptionSource()
    {
        var stackTrace = new StackTrace();
        var frame = stackTrace.GetFrame(1);
        var method = frame?.GetMethod();
        TypeName = method?.ReflectedType?.FullName;
        MethodName = method?.Name;
    }
}

public class ApiHelper
{

    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    protected readonly HttpClient HttpClient;

    public ApiHelper(IHttpClientFactory httpClientFactory)
    {
        HttpClient = httpClientFactory.CreateClient("FluentCMS.Web.UI");
        // TODO: Move this to configuration
        HttpClient.BaseAddress = new Uri("https://localhost:7164/api/");
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<T> Get<T>(string actionName, NameValueCollection query, CancellationToken cancellationToken = default)
    {
        //CaptureExceptionSource();
        var stackTrace = new StackTrace();
        var frame = stackTrace.GetFrame(5);
        var method = frame?.GetMethod();
        TypeName = method?.ReflectedType?.FullName;
        MethodName = method?.Name;
        var url = MethodName; //GetUrl(actionName);

        var response = await HttpClient.GetFromJsonAsync<T>($"{url}?{query}", cancellationToken: cancellationToken);
        return response == null ? throw new AppApiClientException() : response;
    }

    private void CaptureExceptionSource()
    {
        var stackTrace = new StackTrace(1, true);
        var frame = stackTrace.GetFrame(1);
        var method = frame?.GetMethod();
        TypeName = method?.ReflectedType?.FullName;
        MethodName = method?.Name;
    }

}
