namespace FluentCMS.Api.Models;

public class BooleanResponse(bool value) : ApiResult<bool>(value)
{
}
