namespace FluentCMS.Api.Models;

public class RolesResponse(IEnumerable<RoleDto> value) : ApiPagingResult<RoleDto>(value)
{
}
