using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentCMS.Web.Api.Controllers;
using FluentCMS.Web.Api.Models.File;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Web.Api.MappingActions;
public class SetFileUrl(IActionContextAccessor httpContextAccessor, IUrlHelperFactory urlHelperFactory) : IMappingAction<File, FileDetailResponse>
{
    private IUrlHelper urlHelper => urlHelperFactory.GetUrlHelper(httpContextAccessor.ActionContext!);
    public void Process(File source, FileDetailResponse destination, ResolutionContext context)
    {
        var controllerName = nameof(FileController).Replace("Controller", "");
        var actionName = nameof(FileController.Download);

        destination.Url = urlHelper.ActionLink(actionName, controllerName, new { id = source.Id })
                          ?? throw new AppException(ExceptionCodes.FileCouldNotSetUrl);
    }
}
