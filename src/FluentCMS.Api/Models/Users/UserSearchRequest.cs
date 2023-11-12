namespace FluentCMS.Api.Models;

public class UserSearchRequest : PagingRequest
{
    public string? Name { get; set; }
}
