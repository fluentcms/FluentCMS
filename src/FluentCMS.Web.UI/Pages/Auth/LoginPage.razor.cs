using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using FluentCMS.Api.Models;
using FluentCMS.Services.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Pages.Auth;
public partial class LoginPage
{
    [Inject] HttpClient HttpClient { get; set; }
    [Inject] NavigationManager NavigationManager { get; set; }
    public bool Remember { get; set; }
    public UserAuthenticateRequest UserAuthenticateRequest { get; set; } = new()
    {
        Username = "",
        Password = ""
    };
    public async Task Submit()
    {
        //curl - X 'POST' \
        //  'https://localhost:7164/api/Account/Authenticate' \
        //  -H 'accept: application/json' \
        //  -H 'Content-Type: application/json' \
        //  -d '{
        //  "username": "string",
        //  "password": "string"
        //}'
        var response = await HttpClient.PostAsJsonAsync<UserAuthenticateRequest>("/api/Account/Authenticate", UserAuthenticateRequest);
        if (response.IsSuccessStatusCode)
        {
            var userAuthenticationResult = response.Content.ReadFromJsonAsync<UserAuthenticateDto>();
            // persist auth data
            NavigationManager.NavigateTo("/");
        }
    }

}
