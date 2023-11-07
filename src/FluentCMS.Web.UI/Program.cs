using FluentCMS.Api;
using FluentCMS.Api.Identity;
using FluentCMS.Entities.Users;
using FluentCMS.Repositories;
using FluentCMS.Services;
using FluentCMS.Services.Identity;
using FluentCMS.Web.UI;
using FluentCMS.Api.Identity;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

// add FluentCms core
services
    .AddApplicationServices()
    .AddLiteDbRepository(builder.Configuration.GetConnectionString("LiteDb")!);

// Add services to the container.
services.AddRazorComponents()
    .AddInteractiveServerComponents();

services.AddControllers();
services.AddRequestValidation();
services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

services.AddScoped<FluentUserManager>();
services.AddScoped<FluentSignInManager>();

services.AddIdentity<User, Role>()
            .AddUserStore<FluentCmsUserStore>()
            .AddRoleStore<FluentCmsRoleStore>()
            //.AddApiEndpoints()
            ;

services.AddAuthentication().AddBearerToken(o =>
{
    o.BearerTokenExpiration = TimeSpan.FromMinutes(5);
    o.RefreshTokenExpiration = TimeSpan.FromDays(7);
});


services.AddScoped(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor?.HttpContext?.Request;
    var domain = string.Format("{0}://{1}/api/", request?.Scheme, request?.Host.Value);

    return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(domain),
    };
});


var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.Services.SeedDefaultData(@".\SeedData\");
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseRouting();
app.UseStaticFiles();

app.UseAntiforgery();

app.UseApiDocumentation();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    _ = endpoints.MapGet("/", (context) =>
    {
        context.Response.Redirect("/admin");
        return Task.CompletedTask;
    });
});
app.MapControllers();
//app.MapIdentityApi<User>();
app.MapFluentIdentity();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
