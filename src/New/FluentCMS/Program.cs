using FluentCMS.Repositories.MongoDB;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

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

using (var scope = app.Services.CreateScope())
{
    var mongoDb = scope.ServiceProvider.GetRequiredService<IMongoDBContext>();

    foreach (var collectionName in mongoDb.Database.ListCollectionNames().ToList())
    {
        mongoDb.Database.DropCollection(collectionName);
    }

}

app.UseHttpsRedirection();

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

#endregion

app.Run();
