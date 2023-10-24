var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddControllers();

services.AddCmsDocumentation();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCmsDocumentation();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
