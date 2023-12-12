using FluentCMS;
using FluentCMS.Api;
using FluentCMS.Entities;
using FluentCMS.Web.UI;

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

app.UseAntiforgery();

app.UseApiDocumentation();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
