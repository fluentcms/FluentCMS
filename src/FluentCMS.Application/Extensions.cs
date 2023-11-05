using FluentCMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;

namespace FluentCMS.Application;
public static class Extensions
{
    public static FluentCMSBuilder AddApplication(this FluentCMSBuilder fcBuilder)
    {
        // register mediatR
        fcBuilder.Services.AddScoped<IUserService, UserService>();
        fcBuilder.Services.AddScoped<IRoleService, RoleService>();
        fcBuilder.Services.AddScoped<ISiteService, SiteService>();
        fcBuilder.Services.AddScoped<IPageService, PageService>();
        fcBuilder.Services.AddScoped<IContentTypeService, ContentTypeService>();

        // register automapper
        fcBuilder.Services.AddAutoMapper(typeof(Extensions).Assembly);
        fcBuilder.Services.AddValidatorsFromAssembly(typeof(Extensions).Assembly);

        return fcBuilder;
    }
}
