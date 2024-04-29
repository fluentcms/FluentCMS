using FluentCMS.Web.Api.Attributes;
using System.Reflection;

namespace FluentCMS.Web.Api.Controllers;

public class PermissionController : BaseGlobalController
{
    public const string AREA = "Permission Management";
    public const string READ = "Read";
    public const string UPDATE = "Update";
    public const string CREATE = "Create";
    public const string DELETE = "Delete";

    [HttpPut]
    [AuthorizePolicy(AREA, UPDATE)]
    public async Task<IApiResult<RoleDetailResponse>> Update([FromBody][Required] List<PermissionUpdateRequest> request, CancellationToken cancellationToken = default)
    {

        var role = mapper.Map<Role>(request);
        var updated = await roleService.Update(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(updated);
        return Ok(roleResponse);
    }

    [HttpGet]
    public async Task<IApiPagingResult<PolicyResponse>> GetAll(CancellationToken cancellationToken = default)
    {

        var permissionsAreas = new Dictionary<string, PolicyResponse>();

        var assembly = GetType().Assembly;
        var controllerTypes = assembly.GetTypes().Where(x => x.Name.EndsWith("Controller"));

        foreach (var controllerType in controllerTypes)
        {
            foreach (var methodInfo in controllerType.GetMethods())
            {
                var customAttributes = methodInfo.GetCustomAttributes<AuthorizePolicyAttribute>(true);
                if (customAttributes == null)
                    continue;

                foreach (var authorizeAttribute in customAttributes)
                {
                    if (!permissionsAreas.ContainsKey(authorizeAttribute.Area))
                        permissionsAreas.Add(authorizeAttribute.Area, new PolicyResponse { Area = authorizeAttribute.Area, Actions = [] });

                    if (!permissionsAreas[authorizeAttribute.Area].Actions.Where(x => x == authorizeAttribute.Action).Any())
                        permissionsAreas[authorizeAttribute.Area].Actions.Add(authorizeAttribute.Action);
                }
            }
        }

        return await Task.FromResult(OkPaged(permissionsAreas.Values.ToList()));
    }
}


