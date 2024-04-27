using FluentCMS.Web.Api.Attributes;
using System.Reflection;

namespace FluentCMS.Web.Api.Controllers;

public class PermissionController : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiPagingResult<Permission>> GetAll(CancellationToken cancellationToken = default)
    {

        var permissionsAreas = new Dictionary<string, Permission>();

        var assembly = GetType().Assembly;
        var controllerTypes = assembly.GetTypes().Where(x => x.Name.EndsWith("Controller"));

        foreach (var controllerType in controllerTypes)
        {
            foreach (var methodInfo in controllerType.GetMethods())
            {
                var customAttributes = methodInfo.GetCustomAttributes<DynamicAuthorizeAttribute>(true);
                if (customAttributes == null)
                    continue;

                foreach (var authorizeAttribute in customAttributes)
                {
                    if (!permissionsAreas.ContainsKey(authorizeAttribute.Area))
                        permissionsAreas.Add(authorizeAttribute.Area, new Permission { Area = authorizeAttribute.Area, Actions = [] });

                    if (!permissionsAreas[authorizeAttribute.Area].Actions.Where(x => x == authorizeAttribute.Action).Any())
                        permissionsAreas[authorizeAttribute.Area].Actions.Add(authorizeAttribute.Action);
                }
            }
        }

        return await Task.FromResult(OkPaged(permissionsAreas.Values.ToList()));
    }
}

public class Permission
{
    public string Area { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = [];
}
