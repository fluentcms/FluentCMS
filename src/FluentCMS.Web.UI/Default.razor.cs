﻿using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";
    public const string SLOT_ATTRIBUTE = "FluentCMS-Slot";

    public PageFullDetailResponse? Page { get; set; }

    [Parameter]
    public string? Route { get; set; }

    [Inject]
    public NavigationManager NavigationManager { set; get; } = default!;

    [Inject]
    public SetupManager SetupManager { set; get; } = default!;

    [Inject]
    public IHttpClientFactory HttpClientFactory { set; get; } = default!;

    [CascadingParameter]
    public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    public UserLoginResponse? UserLogin { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        NavigationManager.LocationChanged += LocationChanged;
        UserLogin = await AuthenticationStateTask.GetLogin();
    }
    protected override async Task OnParametersSetAsync()
    {
        // check if setup is not done
        // if not it should be redirected to /setup route
        if (!await SetupManager.IsInitialized() && !NavigationManager.Uri.ToLower().EndsWith("/setup"))
        {
            NavigationManager.NavigateTo("/setup", true);
            return;
        }

        var pageClient = HttpClientFactory.CreateApiClient<PageClient>(UserLogin);
        var pageResponse = await pageClient.GetByUrlAsync(NavigationManager.Uri);

        if (pageResponse.Data != null)
            Page = pageResponse.Data;

        await base.OnParametersSetAsync();
    }

    void LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        NavigationManager.LocationChanged -= LocationChanged;
    }

    protected RenderFragment RenderDynamicContent(string content) => builder =>
    {
        if (string.IsNullOrEmpty(content))
            return;

        var processor = new LayoutProcessor(UserLogin ?? new UserLoginResponse());

        var segments = processor.ProcessSegments(content);
        var index = 0;
        foreach (var segment in segments)
        {
            if (segment.GetType() == typeof(HtmlSegment))
            {
                var htmlSegment = segment as HtmlSegment;
                builder.AddContent(index, (MarkupString)htmlSegment!.Content);
            }
            else if (segment.GetType() == typeof(ComponentSegment))
            {
                var componentSegment = segment as ComponentSegment;
                builder.OpenComponent(index, componentSegment!.Type);

                var attributeIndex = 0;
                foreach (var attribute in componentSegment.Attributes)
                {
                    builder.AddComponentParameter(attributeIndex, attribute.Key, attribute.Value);
                    attributeIndex++;
                }

                builder.CloseComponent();
            }
            index++;
        }
    };
}
