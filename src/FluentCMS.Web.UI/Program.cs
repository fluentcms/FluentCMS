using FluentCMS;
using FluentCMS.Application;
using FluentCMS.Repository.LiteDb;
using FluentCMS.Web.UI;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

// add FluentCms core
services.AddFluentCMSCore()
    .AddApplication()
    .AddLiteDbRepository(b =>
    {
        var liteDbFilePath = builder.Configuration.GetConnectionString("LiteDbFile")
            ?? throw new Exception("LiteDb file not defined.");
        Directory.CreateDirectory(Path.GetDirectoryName(liteDbFilePath)!);
        b.SetFilePath(liteDbFilePath);
    });

// Add services to the container.
services.AddRazorComponents()
    .AddInteractiveServerComponents();

services.AddControllers();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

services.AddScoped(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor?.HttpContext?.Request;
    var domain = string.Format("{0}://{1}/api/", request?.Scheme, request?.Host.Value);
    var baseAddress = new Uri(domain);

    return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(domain),
    };
});


var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();

app.UseAntiforgery();

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
