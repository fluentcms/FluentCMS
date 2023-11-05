namespace FluentCMS.Application.Dtos.Users;
public class SearchUserRequest : PagingRequest
{
    public string? Name { get; set; }
}
