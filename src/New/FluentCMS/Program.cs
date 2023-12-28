var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

#region Services

var services = builder.Services;

services.AddSiteServices();

services.AddMongoDbRepositories("MongoDb");

services.AddApiServices();

#endregion

#region App

var app = builder.Build();

app.UseDeveloperExceptionPage();

#if DEBUG

// this section is only for development purposes
// this will delete all data and re-create the database
using var scope = app.Services.CreateScope();
var setup = scope.ServiceProvider.GetRequiredService<FluentCMS.Web.Api.Setup.SetupManager>();
setup.Reset().ConfigureAwait(false).GetAwaiter().GetResult();
//setup.Start("admin", "admin@example.com", "Passw0rd!").ConfigureAwait(false).GetAwaiter().GetResult();

#endif

app.UseHttpsRedirection();

//app.UseDefaultFiles();

app.UseStaticFiles();

app.UseApiDocumentation();

app.UseAntiforgery();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.UseSiteServices();

#endregion

app.Run();
