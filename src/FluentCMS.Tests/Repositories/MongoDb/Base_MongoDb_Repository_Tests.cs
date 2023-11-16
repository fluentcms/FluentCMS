using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.MongoDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Tests.Repositories.MongoDb;
public abstract class Base_MongoDb_Repository_Tests<T> : IDisposable
    where T : IEntity

{
    protected IServiceProvider _serviceProvider;
    private string _tempDbName;
    private string _tempConnectionString;

    public Base_MongoDb_Repository_Tests()
    {
        var services = new ServiceCollection();
        GenerateTempConnection();
        var configuration = new ConfigurationBuilder();
        configuration.AddInMemoryCollection(new KeyValuePair<string, string?>[]
        {
            new ("ConnectionStrings:MongoDb", _tempConnectionString)
        });
        services.AddApplicationServices()
                .AddMongoDbRepositories("MongoDb");
        services.AddTransient<IGenericRepository<T>, MongoDbGenericRepository<T>>();
        services.AddSingleton<IConfiguration>(configuration.Build());
        _serviceProvider = services.BuildServiceProvider();
    }

    private void GenerateTempConnection()
    {
        _tempDbName = $"FluentCMS-TestDb-{Guid.NewGuid():D}";
        _tempConnectionString = $"mongodb://localhost:27017/{_tempDbName}";
    }

    public void Dispose()
    {
        CleanUpDatabase();
    }
    protected IGenericRepository<T> GetRepository()
    {
        return _serviceProvider.GetRequiredService<IGenericRepository<T>>();
    }
    private void CleanUpDatabase()
    {
        var client = new MongoClient(_tempConnectionString);
        client.DropDatabase(_tempDbName);
    }
}
