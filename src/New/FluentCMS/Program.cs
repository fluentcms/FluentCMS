using FluentCMS;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

#region Services

var services = builder.Services;

services.AddControllers();

services.AddApiDocumentation();

services.AddMongoDbRepositories("MongoDb");

services.AddApplicationServices();

services.AddApiServices();

#endregion


#region App

var app = builder.Build();

app.UseHttpsRedirection();

app.UseMiddleware<SetupMiddleware>();

//using var scope = app.Services.CreateScope();
//var setupService = scope.ServiceProvider.GetRequiredService<ISystemSetupService>();
//if (!await setupService.IsInitialized())
//{
//    var systemSettingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
//    var systemSettings = new SystemSettings
//    {
//        Name = "FluentCMS",
//        SuperUsers = new List<string> { "admin" }
//    };
//    await systemSettingsService.Create(systemSettings);
//}

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

#endregion

app.Run();
