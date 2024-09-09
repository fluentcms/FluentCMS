﻿using FluentCMS.Web.ApiClients.Services;

namespace FluentCMS.Web.Plugins.Admin.Auth;

public partial class SetupViewPlugin
{
    public const string FORM_NAME = "SetupForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    public SetupRequest? Model { get; set; }

    [Inject]
    private SetupManager SetupManager { get; set; } = default!;

    [Inject]
    private AuthManager AuthManager { get; set; } = default!;

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;
    private List<string>? Templates { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (await SetupManager.IsInitialized())
            throw new InvalidOperationException("Setup is already complete.");

        if (Templates is null)
        {
            var templatesResponse = await ApiClient.Setup.GetTemplatesAsync();
            Templates = templatesResponse?.Data?.ToList() ?? [];
        }

        Model ??= new SetupRequest
        {
            Username = "admin",
            Email = "admin@example.com",
            Password = "Passw0rd!",
            Url = new Uri(NavigationManager.BaseUri).Authority,
            Template = "Default"
        };
    }

    private async Task OnSubmit()
    {
        if (await SetupManager.Start(Model!))
        {
            await AuthManager.Login(HttpContext, Model!.Username, Model.Password, true);
            NavigateTo("/");
        }
    }
}
