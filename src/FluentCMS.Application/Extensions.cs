﻿using FluentCMS.Application.Sites;
using FluentCMS.Repository;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Application;
public static class Extensions
{
    public static FluentCMSBuilder AddApplication(this FluentCMSBuilder fcBuilder)
    {
        // register mediatR
        //fcBuilder.Services.AddMediatR(c => c.RegisterServicesFromAssembly(typeof(Extensions).Assembly));
        fcBuilder.Services.AddTransient<SiteService>();
        fcBuilder.Services.AddAutoMapper(typeof(Extensions).Assembly);
        fcBuilder.Services.AddScoped<AbstractValidator<SiteDto>, SiteValidator>();

        return fcBuilder;
    }
}
