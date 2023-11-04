using FluentCMS.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

services.AddMongoDbRepositories("MongoDb");

services.AddApplicationServices();

services.AddAutoMapper(typeof(MappingProfiles));

services.AddControllers();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

var app = builder.Build();

app.UseDeveloperExceptionPage();

app.UseHsts();

app.UseHttpsRedirection();

app.UseRouting();

app.UseStaticFiles();

app.UseApiDocumentation();

app.UseAuthorization();

app.MapControllers();

app.Run();
