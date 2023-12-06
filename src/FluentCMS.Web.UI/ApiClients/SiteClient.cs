using FluentCMS.Api.Models;
using System.Web;

namespace FluentCMS.Web.UI.ApiClients;

public class SiteClient(IHttpClientFactory httpClientFactory) : BaseClient(httpClientFactory)
{
    protected override string ControllerName => "Site";

    public async Task<SiteResponse?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["url"] = url;
        var result = await HttpClient.GetFromJsonAsync<ApiResult<SiteResponse>>($"{GetUrl(nameof(GetByUrl))}?{query}", cancellationToken: cancellationToken);
        return result?.Data;
    }
}
