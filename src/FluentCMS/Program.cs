using FluentCMS.Web.Api.Middleware;
using FluentCMS.Web.UI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

#region Services

var services = builder.Services;
var configuration = builder.Configuration;

services.AddApiServices();
services.AddUIComponents();
services.AddCmsServices(configuration);

// Use LiteDB as database
// services.AddLiteDbRepositories("LiteDb");

// Use MongoDB as database
// services.AddMongoDbRepositories("MongoDb");

// Enable caching for repository layer
//services.AddCachedRepositories();

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
