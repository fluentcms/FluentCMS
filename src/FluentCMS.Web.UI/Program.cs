using FluentCMS.Api;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Entities;

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

services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

// TODO: Add JWT to request header, accept-language, etc.
// TODO: Move to somewhere else (Shared project maybe?)
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
