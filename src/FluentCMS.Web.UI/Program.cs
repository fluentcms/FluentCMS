using FluentCMS.Api;
using FluentCMS.Repositories;
using FluentCMS.Services;
using FluentCMS.Web.UI;

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
services.AddScoped<FluentCMS.Api.Middlewares.ErrorMiddleware>();
services.AddRequestValidation();
services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

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

app.UseMiddleware<FluentCMS.Api.Middlewares.ErrorMiddleware>();
app.UseEndpoints(endpoints =>
{
    _ = endpoints.MapGet("/", (context) =>
    {
        context.Response.Redirect("/admin");
        return Task.CompletedTask;
    });
});
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
