using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Http.Extensions;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class SiteClient(IHttpClientFactory httpClientFactory, ApiHelper apiHelper) : BaseClient(httpClientFactory)
{
    protected override string ControllerName => "Site";

    public async Task<ApiPagingResult<SiteResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        return await HttpClient.GetFromJsonAsync<ApiPagingResult<SiteResponse>>($"{GetUrl(nameof(GetAll))}", cancellationToken: cancellationToken);
        //return await Get<ApiPagingResult<SiteResponse>>(nameof(GetAll), cancellationToken);
    }

    public async Task<ApiResult<SiteResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["id"] = id.ToString();
        //return await Get<ApiResult<SiteResponse>>(nameof(GetById), query,cancellationToken);
        return await HttpClient.GetFromJsonAsync<ApiResult<SiteResponse>>($"{GetUrl(nameof(GetById))}?{query}", cancellationToken: cancellationToken);
    }

    public async Task<SiteResponse?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["url"] = url;
        var result = await apiHelper.Get<ApiResult<SiteResponse>>(nameof(GetByUrl), query, cancellationToken);
        //var result = await HttpClient.GetFromJsonAsync<ApiResult<SiteResponse>>($"{GetUrl(nameof(GetByUrl))}?{query}", cancellationToken: cancellationToken);
        //var result = await Get<ApiResult<SiteResponse>>(nameof(GetByUrl), query, cancellationToken);
        return result?.Data;
    }

    public async Task<ApiResult<SiteResponse>> Create(SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var response = await HttpClient.PostAsJsonAsync($"{GetUrl(nameof(Create))}", request, cancellationToken: cancellationToken);
        return await response.Content.ReadFromJsonAsync<ApiResult<SiteResponse>>();
    }

    public async Task<ApiResult<SiteResponse>> Update(SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var response = await HttpClient.PatchAsJsonAsync($"{GetUrl(nameof(Update))}", request, cancellationToken: cancellationToken);
        return await response.Content.ReadFromJsonAsync<ApiResult<SiteResponse>>();
    }

    public async Task<ApiResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["id"] = id.ToString();
        var response = await HttpClient.DeleteAsync($"{GetUrl(nameof(Delete))}?{query}", cancellationToken: cancellationToken);
        return await response.Content.ReadFromJsonAsync<ApiResult>();
    }
}
