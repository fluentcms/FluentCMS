namespace FluentCMS.Api.Plugins.IdentityManagement;

[Plugin]
public class IdentityManagerPlugin : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddAutoMapper(cfg => { }, typeof(MappingProfile));

        services.AddEntityFrameworkIdentity<User, Role>();

        // Configure Identity options from appsettings.json
        services.AddDbOptions<JwtOptions>("JwtOptions");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
      .AddJwtBearer();


        // TODO: implement here the token validation parameters configuration
        //builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (jwtBearerOptions, sp) =>
        //{
        //    var jwtOptions = sp.GetRequiredService<IOptionsSnapshot<JwtOptions>>().Value;
        //    options.TokenValidationParameters = new TokenValidationParameters
        //    {
        //        ValidateIssuer = validationParams.ValidateIssuer,
        //        ValidateAudience = validationParams.ValidateAudience,
        //        ValidateLifetime = validationParams.ValidateLifetime,
        //        ValidateIssuerSigningKey = validationParams.ValidateIssuerSigningKey,
        //        RequireExpirationTime = validationParams.RequireExpirationTime,
        //        RequireSignedTokens = validationParams.RequireSignedTokens,
        //        ClockSkew = validationParams.ClockSkew,
        //        ValidIssuer = jwtOptions.Issuer,
        //        ValidAudience = jwtOptions.Audience,
        //        IssuerSigningKey = new SymmetricSecurityKey(SHA512.HashData(Encoding.UTF8.GetBytes(jwtOptions.Secret)))
        //    };
        //});
    }


    public void Configure(IApplicationBuilder app)
    {
    }
}
