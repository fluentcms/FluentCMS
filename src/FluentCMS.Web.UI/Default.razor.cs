﻿namespace FluentCMS.Web.UI;

public partial class Default
{
    protected override async Task OnInitializedAsync()
    {
        if (!await SetupManager.IsInitialized())
            NavigationManager.NavigateTo("/setup", true);

        await base.OnInitializedAsync();
    }
}
