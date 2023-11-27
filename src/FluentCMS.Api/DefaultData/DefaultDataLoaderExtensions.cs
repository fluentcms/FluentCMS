﻿using FluentCMS.Entities;
using FluentCMS.Repositories.MongoDB;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System.Text.Json;

namespace FluentCMS.Api;

public static class DefaultDataLoaderExtensions
{
    public static void ResetMongoDb(this IServiceProvider provider)
    {
        var scope = provider.CreateScope();

        var mongoDb = scope.ServiceProvider.GetRequiredService<IMongoDBContext>();

        foreach (var collectionName in mongoDb.Database.ListCollectionNames().ToList())
        {
            mongoDb.Database.DropCollection(collectionName);
        }
    }

    public static void LoadInitialDataFrom(this IServiceProvider provider, string dataFolder)
    {
        var scope = provider.CreateScope();

        var hostService = scope.ServiceProvider.GetRequiredService<IHostService>();
        var siteService = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            var defaultData = LoadData<DefaultData>($@"{dataFolder}\default.json");

            var superUser = new User
            {
                UserName = defaultData.SuperAdmin.UserName,
                Email = defaultData.SuperAdmin.Email
            };

            appContext.Current = new CurrentContext
            {
                UserName = superUser.UserName,
                IsSuperAdmin = true
            };

            // Default users creation
            userService.Create(superUser, defaultData.SuperAdmin.Password).GetAwaiter().GetResult();

            // Host creation
            hostService.Create(defaultData.Host).GetAwaiter().GetResult();

            // Site creation            
            siteService.Create(defaultData.Site).GetAwaiter().GetResult();
                        
            // Pages creation: adding a few default pages
            foreach (var page in defaultData.Pages)
            {
                page.SiteId = defaultData.Site.Id;
                pageService.Create(page).GetAwaiter().GetResult();
            }
        }
    }

    private static readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

    private static T LoadData<T>(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            var result = JsonSerializer.Deserialize<T>(json, _serializerOptions);

            return result == null ?
                throw new Exception($"Unable to load seed data from {jsonFile}") : result;
        }
        catch (Exception)
        {
            throw new Exception($"Unable to load seed data from {jsonFile}");
        }
    }
}
