using FluentCMS.Web.Api.Attributes;
using System.Reflection;

namespace FluentCMS.Web.Api.Controllers;

public class RoleController(IMapper mapper, IRoleService roleService) : BaseGlobalController
{
    public const string AREA = "Role Management";
    public const string READ = "Read";
    public const string UPDATE = "Update";
    public const string CREATE = "Create";
    public const string DELETE = "Delete";

    [HttpGet]
    [AuthorizePolicy(AREA, READ)]
    public async Task<IApiPagingResult<RoleDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(cancellationToken);
        var roleResponses = mapper.Map<IEnumerable<RoleDetailResponse>>(roles);
        return OkPaged(roleResponses);
    }

    [HttpPost]
    [AuthorizePolicy(AREA, CREATE)]
    public async Task<IApiResult<RoleDetailResponse>> Create([FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(newRole);
        return Ok(roleResponse);
    }

    [HttpPut]
    [AuthorizePolicy(AREA, UPDATE)]
    public async Task<IApiResult<RoleDetailResponse>> Update([FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var updated = await roleService.Update(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(updated);
        return Ok(roleResponse);
    }

    [HttpDelete("{id}")]
    [AuthorizePolicy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await roleService.Delete(id, cancellationToken);
        return Ok(true);
    }

    [HttpGet]
    public async Task<IApiPagingResult<Policy>> GetAvailablePolicies(CancellationToken cancellationToken = default)
    {

        var policiesDict = new Dictionary<string, Policy>();

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
                    if (!policiesDict.ContainsKey(authorizeAttribute.Area))
                        policiesDict.Add(authorizeAttribute.Area, new Policy { Area = authorizeAttribute.Area, Actions = [] });

                    if (!policiesDict[authorizeAttribute.Area].Actions.Where(x => x == authorizeAttribute.Action).Any())
                        policiesDict[authorizeAttribute.Area].Actions.Add(authorizeAttribute.Action);
                }
            }
        }

        return await Task.FromResult(OkPaged(policiesDict.Values.ToList()));
    }
}
