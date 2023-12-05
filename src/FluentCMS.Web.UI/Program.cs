using FluentCMS.Api;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Entities;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

services.AddApplicationServices();

services.AddMongoDbRepositories("MongoDb");

services.AddJwtTokenProvider(builder.Configuration);
services.AddSmtpEmailProvider();

services.AddAuthentication();
services.AddAuthorization();

services.AddUIServices();

services.AddScoped<IApplicationContext, ApiApplicationContext>();

services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonContentConverter<Content>());
    options.JsonSerializerOptions.Converters.Add(new JsonContentConverter<PluginContent>());
    //options.JsonSerializerOptions.Converters.Add(new DictionaryStringObjectJsonConverter());
});

services.AddRequestValidation();

services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

services.AddScoped<AuthenticationStateProvider, FluentCmsAuthenticationStateProvider>();

// TODO: Add to request header, accept-language, etc.
// TODO: Move to somewhere else (Shared project maybe?)
services.AddScoped(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor?.HttpContext?.Request;
    var domain = string.Format("{0}://{1}/api/", request?.Scheme, request?.Host.Value);

    //fetch AccessToken from cookies
    var accessToken = httpContextAccessor?.HttpContext?.Request.Cookies["access-token"];

    var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(domain),
    };

    // if access-token in cookies
    if (!string.IsNullOrEmpty(accessToken))
    {
        // set access-token
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }

    return client;
});

// Register NSwag Generated Clients
#region NSwag
//FluentCMS.Web.UI.AccountClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new AccountClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.HostClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new HostClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.PageClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new PageClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.PluginClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new PluginClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.RoleClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new RoleClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.SiteClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new SiteClient(baseUrl, httpClient);
});
//FluentCMS.Web.UI.UserClient
services.AddScoped(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var baseUrl = httpClient.BaseAddress?.ToString();
    return new UserClient(baseUrl, httpClient);
});
#endregion


services.AddJwtAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Services.ResetMongoDb();
    app.Services.LoadInitialDataFrom(@"./DefaultData/");

    app.UseDeveloperExceptionPage();
}

app.UseHsts();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAntiforgery();

app.UseApiDocumentation();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
