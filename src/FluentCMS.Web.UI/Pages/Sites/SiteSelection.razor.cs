using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Pages.Sites;
public partial class SiteSelection
{
    [Inject] HttpClient HttpClient { get; set; }
    public IApiPagingResult<SiteResponse> Response { get; set; } = ApiPagingResult<SiteResponse>.Empty;
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

            Response = (await HttpClient.GetFromJsonAsync<ApiPagingResult<SiteResponse>>($"Site/GetAll"))!;

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
}
