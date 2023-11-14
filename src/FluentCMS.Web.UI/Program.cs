using FluentCMS.Api;
using FluentCMS;
using FluentCMS.Web.UI;
using FluentCMS.Providers.Identity;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfig(builder.Environment);

var services = builder.Services;

services.AddApplicationServices();

services.AddMongoDbRepositories("MongoDb");

services.AddJwtTokenProvider(builder.Configuration);
services.AddSmtpEmailProvider();

services.AddAuthentication();
services.AddAuthorization();

// Add services to the container.
services.AddRazorComponents()
    .AddInteractiveServerComponents();

services.AddScoped<IApplicationContext, ApiApplicationContext>();
services.AddControllers();
services.AddRequestValidation();
services.AddMappingProfiles();

services.AddApiDocumentation();

services.AddHttpContextAccessor();

services.AddScoped(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor?.HttpContext?.Request;
    var domain = string.Format("{0}://{1}/api/", request?.Scheme, request?.Host.Value);

    return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(domain),
    };
});


#region Identity

var options = builder.Configuration.GetInstance<JwtOptions>("Jwt");

services.AddAuthentication(configureOptions =>
{
    var defaultScheme = "Bearer";
    configureOptions.DefaultAuthenticateScheme = defaultScheme;
    configureOptions.DefaultScheme = defaultScheme;
    configureOptions.DefaultChallengeScheme = defaultScheme;
})
    .AddJwtBearer(jwt =>
    {
        var key = Encoding.UTF8.GetBytes(options.Secret);
        jwt.SaveToken = true;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidateAudience = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            SaveSigninToken = true,
            ValidAudience = options.Audience,
            ValidIssuer = options.Issuer
        };
    });

#endregion

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.Services.SeedDefaultData(@".\SeedData\");
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseRouting();
app.UseStaticFiles();

app.UseAntiforgery();

app.UseApiDocumentation();

app.UseAuthentication();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    _ = endpoints.MapGet("/", (context) =>
    {
        context.Response.Redirect("/admin");
        return Task.CompletedTask;
    });
});
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
