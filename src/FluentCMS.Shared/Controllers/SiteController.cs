using FluentCMS.Application.Sites;
using FluentCMS.Entities.Sites;
using FluentCMS.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FluentCMS.Models;
using FluentCMS.Application;

namespace FluentCMS.Shared.Controllers;
[ValidationActionFilter]
public class SiteController : GenericCrudController<Site, SiteDto>
{
    public SiteController(IServiceProvider serviceProvider, SiteService siteService) : base(serviceProvider, siteService)
    {
    }
}
