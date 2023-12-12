using FluentCMS.Entities;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;

namespace FluentCMS.Web.UI.ApiClients;

public abstract class BaseClient
{
    private string _typeName = string.Empty;
    private string _methodName = string.Empty;

    protected readonly HttpClient HttpClient;

    private static JsonSerializerOptions _options = new()
    {
        Converters = { new JsonContentConverter<PluginContent>(), new JsonContentConverter<Content>() },
    };

    protected BaseClient(IHttpClientFactory httpClientFactory)
    {
        HttpClient = httpClientFactory.CreateClient("FluentCMS.Web.Api");
    }

    protected async Task<T> Call<T>(object request, CancellationToken cancellationToken = default)
    {
        CaptureCallerMethodSource();

        if (_methodName.StartsWith("create", StringComparison.CurrentCultureIgnoreCase))
        {
            var response = await HttpClient.PostAsJsonAsync($"{GetEndpointUrl()}", request, _options, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else if (_methodName.StartsWith("update", StringComparison.CurrentCultureIgnoreCase))
        {
            var response = await HttpClient.PatchAsJsonAsync($"{GetEndpointUrl()}", request, _options, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else if (_methodName.StartsWith("delete", StringComparison.CurrentCultureIgnoreCase))
        {
            var response = await HttpClient.DeleteAsync($"{GetEndpointUrl()}", cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else
        {
            throw new Exception($"Unable to determine method type in client class {GetType().Name} method Call");
        }
    }

    protected async Task<T> Call<T>(string contentType, object request, CancellationToken cancellationToken = default)
    {
        CaptureCallerMethodSource();

        if (_methodName.StartsWith("create", StringComparison.CurrentCultureIgnoreCase))
        {

            var response = await HttpClient.PostAsJsonAsync($"{GetEndpointUrl(contentType)}", request, _options, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else if (_methodName.StartsWith("update", StringComparison.CurrentCultureIgnoreCase))
        {
            var response = await HttpClient.PatchAsJsonAsync($"{GetEndpointUrl(contentType)}", request, _options, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else if (_methodName.StartsWith("delete", StringComparison.CurrentCultureIgnoreCase))
        {
            var response = await HttpClient.DeleteAsync($"{GetEndpointUrl(contentType)}", cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new AppApiClientException();
        }
        else
        {
            throw new Exception($"Unable to determine method type in client class {GetType().Name} method Call");
        }
    }

    protected async Task<T> Call<T>(NameValueCollection query, CancellationToken cancellationToken = default)
    {
        CaptureCallerMethodSource();

        if (_methodName.StartsWith("get", StringComparison.CurrentCultureIgnoreCase))
        {
            return await HttpClient.GetFromJsonAsync<T>($"{GetEndpointUrl()}?{query}", cancellationToken)
                ?? throw new AppApiClientException();
        }
        else
        {
            throw new Exception($"Unable to determine method type in client class {GetType().Name} method Call");
        }
    }

    protected async Task<T> Call<T>(string contentType, NameValueCollection query, CancellationToken cancellationToken = default)
    {
        CaptureCallerMethodSource();

        if (_methodName.StartsWith("get", StringComparison.CurrentCultureIgnoreCase))
        {
            return await HttpClient.GetFromJsonAsync<T>($"{GetEndpointUrl(contentType)}?{query}", cancellationToken)
                ?? throw new AppApiClientException();
        }
        else
        {
            throw new Exception($"Unable to determine method type in client class {GetType().Name} method Call");
        }
    }

    protected async Task<T> Call<T>(CancellationToken cancellationToken = default)
    {
        CaptureCallerMethodSource();

        var response = await HttpClient.GetFromJsonAsync<T>($"{GetEndpointUrl()}", cancellationToken);
        return response ?? throw new AppApiClientException();
    }

    private string GetEndpointUrl(string? contentType = default)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return $"{_typeName.Replace("Client", string.Empty)}/{_methodName}";
        else
            return $"{_typeName.Replace("Client", string.Empty)}/{contentType}/{_methodName}";
    }

    private void CaptureCallerMethodSource()
    {
        var stackTrace = new StackTrace();

        var frame = stackTrace.GetFrame(6) ??
            throw new Exception($"Unable to capture exception source in client class {GetType().Name} method CaptureExceptionSource");

        var method = frame?.GetMethod() ??
            throw new Exception($"Unable to capture exception source in client class {GetType().Name} method CaptureExceptionSource");

        _typeName = method?.ReflectedType?.Name ??
            throw new Exception($"Unable to capture exception source in client class {GetType().Name} method CaptureExceptionSource");

        _methodName = method?.Name ??
            throw new Exception($"Unable to capture exception source in client class {GetType().Name} method CaptureExceptionSource");
    }
}

