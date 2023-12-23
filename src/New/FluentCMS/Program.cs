using FluentCMS.Web.Api;
using FluentCMS.Web.Api.Extentions;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

#region Services

var services = builder.Services;

services.AddApiDocumentation();

services.AddMongoDbRepositories("MongoDb");

services.AddApplicationServices();

services.AddApiServices();

services.AddAutoMapper();

#endregion

#region App

var app = builder.Build();

app.UseDeveloperExceptionPage();

#if DEBUG

// this section is only for development purposes
// this will delete all data and re-create the database
using var scope = app.Services.CreateScope();
var setup = scope.ServiceProvider.GetRequiredService<SetupManager>();
//setup.Reset().ConfigureAwait(false).GetAwaiter().GetResult();
//setup.Start().ConfigureAwait(false).GetAwaiter().GetResult();

#endif

app.UseHttpsRedirection();

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

#endregion

app.Run();
