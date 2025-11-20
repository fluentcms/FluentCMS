using System.Net.Http.Json;
using System.Text.Json;
using Admin.Api.ApiModels;

namespace Admin.Api;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("http://localhost:5093");
    }

    private async Task<T> HandleResponse<T>(HttpResponseMessage response) where T : ApiResponseBase
    {
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(response.StatusCode, new List<string> { content });
        }

        var apiResp = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (apiResp == null)
            throw new ApiException(response.StatusCode, new List<string> { "Invalid API response" });

        if (!apiResp.Success)
        {
            var errors = apiResp.Errors?.Select(e => e.Description).ToList()
                         ?? new List<string> { "Unknown API error" };

            throw new ApiException((System.Net.HttpStatusCode)apiResp.StatusCode, errors);
        }

        return apiResp;
    }

    public async Task<ListApiResponse<T>> GetListAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        return await HandleResponse<ListApiResponse<T>>(response);
    }
     public async Task<ApiResponse<T>> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        return await HandleResponse<ApiResponse<T>>(response);
    }


    public async Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest body)
    {
        var response = await _http.PostAsJsonAsync(url, body);
        return await HandleResponse<ApiResponse<TResponse>>(response);
    }

    public async Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest body)
    {
        var response = await _http.PutAsJsonAsync(url, body);
        return await HandleResponse<ApiResponse<TResponse>>(response);
    }

    public async Task<ApiResponse<TResponse>> DeleteAsync<TResponse>(string url)
    {
        var response = await _http.DeleteAsync(url);
        return await HandleResponse<ApiResponse<TResponse>>(response);
    }
}

public class ApiException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public List<string> Errors { get; }

    public ApiException(System.Net.HttpStatusCode statusCode, List<string> errors)
        : base($"API Error ({(int)statusCode}): {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}

