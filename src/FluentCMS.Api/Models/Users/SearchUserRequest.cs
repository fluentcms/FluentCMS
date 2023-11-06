namespace FluentCMS.Api.Models.Users;

public class SearchUserRequest : PagingRequest
{
    public string? Name { get; set; }
}
