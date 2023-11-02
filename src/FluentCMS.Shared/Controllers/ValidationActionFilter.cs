using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentCMS.Shared.Controllers;
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false)]
public class ValidationActionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        base.OnException(context);
        if(context.Exception is ValidationException)
        {
            var validationException = (ValidationException)context.Exception;
            context.Result = new BadRequestObjectResult(validationException.Errors);
        }
        if (context.Exception is ApplicationException)
        {
            var validationException = (ApplicationException)context.Exception;
            context.Result = new BadRequestObjectResult(validationException.Message);
        }
    }

}
