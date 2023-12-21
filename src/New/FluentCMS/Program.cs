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

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

#endregion

app.Run();
