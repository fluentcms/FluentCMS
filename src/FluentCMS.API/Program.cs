var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

services.AddControllers();

services.AddApiDocumentation();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseApiDocumentation();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
