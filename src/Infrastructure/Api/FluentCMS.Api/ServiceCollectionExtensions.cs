namespace FluentCMS.Api;

public static class ServiceCollectionExtensions
{
    public const string SESSION_ID_HEADER_KEY = "X_Session_Id";
    public const string UNIQUE_USER_ID_HEADER_KEY = "X-Unique-Id";
    public const string DEFAULT_LANGUAGE = "en-US";

    public static IServiceCollection AddFluentCmsApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddExecutionContext();

        //services.AddApplicationServices();
        services.AddOptions<JwtOptions>()
            .BindConfiguration("JwtOptions");

        services
            .AddControllers(config =>
            {
                config.Filters.Add<ApiResultValidateModelFilter>();
                config.Filters.Add<ApiResultExceptionFilter>();
                config.Filters.Add<ApiResultActionFilter>();
            })
            .AddJsonOptions(options =>
            {
                //options.JsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // This is already enabled by default with [ApiController]
                options.SuppressModelStateInvalidFilter = true;
            });


        services.AddApiDocumentation();
        return services;
    }

    public static WebApplication UseFluentCmsApi(this WebApplication app)
    {
        app.UseApiDocumentation();

        app.UseAuthentication();

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), app =>
        {
            // this will be executed only when the path starts with "/api"
            app.UseMiddleware<JwtAuthorizationMiddleware>();
        });

        app.UseAuthorization();

        app.MapControllers();

        return app;
    }

    private static string _applicationName = string.Empty;
    private static string _applicationVersion = string.Empty;

    private static IServiceCollection AddApiDocumentation(this IServiceCollection services, string applicationName = "FluentCMS API", string version = "v1.0.0")
    {
        _applicationName = applicationName;
        _applicationVersion = version;


        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = applicationName, Version = version });

            // Define the security scheme for bearer tokens
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            c.OrderActionsBy((apiDesc) => $"{apiDesc.ActionDescriptor.RouteValues["controller"]}");
        });

        return services;
    }

    private static IApplicationBuilder UseApiDocumentation(this IApplicationBuilder app)
    {
        // Enable middleware to serve generated Swagger as a JSON endpoint
        app.UseSwagger();

        // Enable middleware to serve Swagger UI
        app.UseSwaggerUI(c =>
        {
            c.DisplayRequestDuration();
            c.SwaggerEndpoint("/swagger/v1/swagger.json", _applicationName + " " + _applicationVersion);
            c.RoutePrefix = "api/doc";
            c.DocExpansion(DocExpansion.None);
        });

        return app;
    }

    private static IServiceCollection AddExecutionContext(this IServiceCollection services)
    {
        services.AddScoped<IApplicationExecutionContext>(sp =>
        {
            // Constants for HTTP header keys used to retrieve session and unique user identifiers
            var accessor = sp.GetRequiredService<IHttpContextAccessor>() ??
                throw new InvalidOperationException("HttpContextAccessor not found.");

            var context = accessor?.HttpContext;

            if (context == null)
                return new SystemExecutionContext();

            var instance = new ApiExecutionContext
            {
                // Initialize properties based on the current HTTP context
                TraceId = context.TraceIdentifier,
                UniqueId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(UNIQUE_USER_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty,
                SessionId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(SESSION_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty,
                UserIp = context.Connection?.RemoteIpAddress?.ToString() ?? string.Empty,
                Language = context.Request?.GetTypedHeaders().AcceptLanguage.FirstOrDefault()?.Value.Value ?? DEFAULT_LANGUAGE,
            };

            // Retrieve the user claims principal from the context
            var user = accessor.HttpContext?.User;

            if (user != null)
            {
                // Extract and parse the user ID from claims (ClaimTypes.Sid)
                var idClaimValue = user.FindFirstValue(ClaimTypes.Sid);
                instance.UserId = idClaimValue == null ? null : Guid.Parse(idClaimValue);

                // Extract the username from claims (ClaimTypes.NameIdentifier)
                instance.Username = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

                // Determine if the user is authenticated
                instance.IsAuthenticated = user.Identity?.IsAuthenticated ?? false;
            }
            return instance;
        });

        return services;
    }
}
