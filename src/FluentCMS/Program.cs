var builder = WebApplication.CreateBuilder(args);

#region Services

var services = builder.Services;
var configuration = builder.Configuration;

services.AddApiServices();
services.AddUIComponents();
services.AddCmsServices(configuration);

// Use LiteDB as database
//services.AddLiteDbRepositories("LiteDb");

// Use MongoDB as database
//services.AddMongoDbRepositories("MongoDb");

// Use Sqlite as database
services.AddSqliteRepositories("Sqlite");

// Use SqlServer as database
//services.AddSqlServerRepositories("SqlServer");

// Use MySql as database
//services.AddMySqlRepositories("MySql");

// Use PostgreSQL as database
//services.AddPostgresRepositories("PostgreSQL");

// Enable caching for repository layer
services.AddCachedRepositories();

#endregion

#region Providers

// register providers
services.AddJwtApiTokenProvider();
services.AddSmtpEmailProvider();
services.AddInMemoryMessageBusProvider();
services.AddLocalFileStorageProvider();
services.AddScribanTemplateRenderingProvider();
services.AddInMemoryCacheProvider();

#endregion

#region App

var app = builder.Build();

app.UseDeveloperExceptionPage();

app.UseHttpsRedirection();

app.UseRemoteStaticFileServices();

app.UseStaticFiles();

app.UseApiService();

app.UseCmsServices();

#endregion

app.Run();
