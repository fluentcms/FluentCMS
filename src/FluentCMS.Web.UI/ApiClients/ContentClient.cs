using FluentCMS.Api.Models;
using FluentCMS.Entities;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class ContentClient(
    IHttpClientFactory httpClientFactory) :
    BaseClient(httpClientFactory)
{
    public async Task<Content> Create(Content content, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<Content>>(content.Type, content, cancellationToken);
        return result.Data;
    }

    public async Task<Content> Update(Content content, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<Content>>(content.Type, content, cancellationToken);
        return result.Data;
    }

    //public async Task Delete(string contentType, IdRequest request, CancellationToken cancellationToken = default)
    //{
    //    await pluginContentService.Delete(request.SiteId, contentType, request.Id, cancellationToken);
    //}

    public async Task<ApiPagingResult<Content>> GetAll(string contentType, Guid siteId, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["siteId"] = siteId.ToString();
        var apiResponse = await Call<ApiPagingResult<Content>>(contentType, query, cancellationToken);
        return apiResponse;
    }
}
