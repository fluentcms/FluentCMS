using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Pages.Sites;
public partial class SiteSelection
{
    [Inject] HttpClient HttpClient { get; set; }
    public IApiPagingResult<SiteResponse> Response { get; set; } = ApiPagingResult<SiteResponse>.Empty;
    public SiteSearchRequest Request { get; set; } = new() { PageSize = 15 };
    public bool IsLoading { get; set; } = false;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Refresh();
            StateHasChanged();
        }
    }
    public async Task Refresh()
    {
        IsLoading = true;
        try
        {
            //curl - X 'GET' \
            //  'https://localhost:7164/api/Site/GetAll?PageIndex=0&PageSize=15' \
            //  -H 'accept: application/json'

            //Response = await HttpClient.GetFromJsonAsync<ApiPagingResult<SiteResponse>>($"api/Site/GetAll?PageIndex={Request.PageIndex}&PageSize={Request.PageSize}");

            // auth is not implemented yet use fake data with 30 sites
            var sites = Enumerable.Range(1, 30).Select(x => (index: x, id: Guid.NewGuid())).Select(x => new SiteResponse()
            {
                CreatedAt = DateTime.Now,
                Id = x.id,
                Name = $"Site {x.index}",
                Urls = [$"https://site{x.index}.com"],
                LastUpdatedAt = DateTime.Now,
                LastUpdatedBy = "admin",
                CreatedBy = "admin",
                Description = "Lorem ipsum",
            }).Skip(Request.PageIndex * Request.PageSize).Take(Request.PageSize);
            Response = new ApiPagingResult<SiteResponse>(sites,Request.PageIndex,Request.PageSize,30);
        }
        catch (Exception)
        {

            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task GoToPage(int pageIndex)
    {
        Request.PageIndex = pageIndex;
        await Refresh();
    }
}
