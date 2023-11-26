using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using FluentCMS.Api.Models;
using FluentCMS.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluentCMS.Web.UI.Pages.Auth;
public partial class LoginPage
{
    [Inject] HttpClient HttpClient { get; set; }
    [Inject] NavigationManager NavigationManager { get; set; }
    [Inject] ProtectedLocalStorage ProtectedLocalStorage { get; set; }
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
            var userAuthenticationResult = await response.Content.ReadFromJsonAsync<ApiResult<UserAuthenticateDto>>();
            // persist auth data
            if (userAuthenticationResult?.Data != null)
            {
                await ProtectedLocalStorage.SetAsync("access-token", userAuthenticationResult.Data.Token);
                await ProtectedLocalStorage.SetAsync("user-id", userAuthenticationResult.Data.UserId);
                await ProtectedLocalStorage.SetAsync("role-ids", userAuthenticationResult.Data.RoleIds);
            }
            NavigationManager.NavigateTo("/");
        }
    }

}
