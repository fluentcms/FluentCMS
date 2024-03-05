using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FluentCMS.Web.UI.Services;
public static class ErrorMessageExtension
{
    public const string ErrorMessageFactoryKey = "ErrorMessageExtension";

    public static Func<Exception, string[]> ErrorMessageFactory = (Exception ex) =>
    {
        if (ex is ApiClientException clientException)
        {
            return clientException.Data?.Errors?.Select(x => x.Code).DefaultIfEmpty(clientException.Message).ToArray() ?? [clientException.Message];
        }
        else
        {
            return [ex.ToString()];
        }
    };
    

    public static IServiceCollection AddErrorMessageFactory(this IServiceCollection services)
    {
        services.AddKeyedSingleton(ErrorMessageFactoryKey, ErrorMessageFactory);
        return services;
    }
}
