using FluentCMS;
using FluentCMS.Api;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.Resources;
using System.Resources;

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
    //options.JsonSerializerOptions.Converters.Add(new JsonContentConverter<Content>());
    //options.JsonSerializerOptions.Converters.Add(new JsonContentConverter<PluginContent>());
    //options.JsonSerializerOptions.Converters.Add(new DictionaryStringObjectJsonConverter());
});

services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

services.AddJwtAuthentication(builder.Configuration);

// register IconResourceManager
services.AddKeyedScoped(typeof(Icons).FullName, (_, _) =>
{
    var iconsType = typeof(Icons);
    return new ResourceManager(iconsType.FullName!, iconsType.Assembly);
});


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

// we need this for testing, Program.cs is Internal by default! this change will set its access modifier to public! so we can reference this in our e2e Test
public partial class Program
{
    
}
