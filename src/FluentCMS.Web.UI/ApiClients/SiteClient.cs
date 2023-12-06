using FluentCMS.Api.Models;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class SiteClient(
    IHttpClientFactory httpClientFactory) :
    BaseClient(httpClientFactory)
{
    public async Task<IEnumerable<SiteResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiPagingResult<SiteResponse>>(cancellationToken);
        return result?.Data ?? Enumerable.Empty<SiteResponse>();
    }

    public async Task<SiteResponse?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["id"] = id.ToString();
        var result = await Call<ApiResult<SiteResponse>>(query, cancellationToken);
        return result?.Data;
    }

    public async Task<SiteResponse?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["url"] = url;
        var result = await Call<ApiResult<SiteResponse>>(query, cancellationToken);
        return result?.Data;
    }

    public async Task<SiteResponse?> Create(SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<SiteResponse>>(request, cancellationToken: cancellationToken);
        return result?.Data;
    }

    public async Task<SiteResponse?> Update(SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<SiteResponse>>(request, cancellationToken: cancellationToken);
        return result?.Data;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await Call<ApiResult>(id, cancellationToken: cancellationToken);
    }
}
