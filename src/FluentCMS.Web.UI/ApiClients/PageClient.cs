using FluentCMS.Api.Models;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class PageClient(IHttpClientFactory httpClientFactory) : BaseClient(httpClientFactory)
{
    protected override string ControllerName => "Page";

    public async Task<PageResponse?> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["siteId"] = siteId.ToString();
        query["path"] = path;
        var result = await HttpClient.GetFromJsonAsync<ApiResult<PageResponse>>($"{GetUrl(nameof(GetByPath))}?{query}", cancellationToken: cancellationToken);
        return result?.Data;
    }
}
