using FluentCMS.Api.Models;
using FluentCMS.Entities;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class PluginContentClient(
    IHttpClientFactory httpClientFactory) :
    BaseClient(httpClientFactory)
{

    //public async Task<PluginContent> Create(string contentType, PluginContent content, CancellationToken cancellationToken = default)
    //{
    //    content.Type = contentType;
    //    var newContent = await pluginContentService.Create(content, cancellationToken);
    //    return new ApiResult<PluginContent>(newContent);
    //}

    //public async Task<PluginContent> Update(string contentType, PluginContent content, CancellationToken cancellationToken = default)
    //{
    //    content.Type = contentType;
    //    var updatedContent = await pluginContentService.Update(content, cancellationToken);
    //    return new ApiResult<PluginContent>(updatedContent);
    //}

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
