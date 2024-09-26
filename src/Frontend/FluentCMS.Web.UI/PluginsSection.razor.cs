﻿using Microsoft.AspNetCore.Components.Routing;

namespace FluentCMS.Web.UI;

public partial class PluginsSection : IDisposable
{
    [Parameter]
    // this will be set while dynamically rendering the template
    public string Name { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { set; get; } = default!;

    private void ViewStateChanged(object? sender, EventArgs e)
    {
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        ViewState.OnStateChanged -= ViewStateChanged;
        NavigationManager.LocationChanged -= LocationChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        NavigationManager.LocationChanged += LocationChanged;
        ViewState.OnStateChanged += ViewStateChanged;
        await Task.CompletedTask;
    }

    void LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        ViewState.Reload();
        StateHasChanged();
    }
}
