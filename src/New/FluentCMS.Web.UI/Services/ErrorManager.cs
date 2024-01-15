using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Web.UI.Services;
public class ErrorManager(IServiceProvider serviceProvider)
{
    public async Task HandleException(Exception ex)
    {
        var exceptionType = ex.GetType();
        IErrorHandler? handler = null;
        var explicitHandlerMethod = typeof(ErrorManager)
            .GetMethod(nameof(FindExplicitHandler),System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(ex.GetType());
        handler = (IErrorHandler?) explicitHandlerMethod.Invoke(this, null);
        if (handler == null)
        {
            handler = FindFallBackHandler();
        }
        await handler.HandleException(ex);
    }

    private IErrorHandler FindFallBackHandler()
    {
        return serviceProvider.GetRequiredService<IErrorHandler>();
    }

    private IErrorHandler<T>? FindExplicitHandler<T>()
        where T : Exception
    {
        return serviceProvider.GetService<IErrorHandler<T>>();
    }
}
