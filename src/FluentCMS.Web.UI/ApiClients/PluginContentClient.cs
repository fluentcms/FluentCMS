using FluentCMS.Api.Models;
using FluentCMS.Entities;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class PluginContentClient(
    IHttpClientFactory httpClientFactory) :
    BaseClient(httpClientFactory)
{

    public async Task<PluginContent> Create(PluginContent content, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<PluginContent>>(content.Type, content, cancellationToken);
        return result.Data;
    }

    public async Task<PluginContent> Update(PluginContent content, CancellationToken cancellationToken = default)
    {
        var result = await Call<ApiResult<PluginContent>>(content.Type, content, cancellationToken);
        return result.Data;
    }

    //public async Task Delete(string contentType, IdRequest request, CancellationToken cancellationToken = default)
    //{
    //    await pluginContentService.Delete(request.SiteId, contentType, request.Id, cancellationToken);
    //}

    public async Task<ApiPagingResult<PluginContent>> GetByPluginId(string contentType, Guid siteId, Guid pluginId, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["siteId"] = siteId.ToString();
        query["pluginId"] = pluginId.ToString();
        var apiResponse = await Call<ApiPagingResult<PluginContent>>(contentType, query, cancellationToken);
        return apiResponse;
    }
}
