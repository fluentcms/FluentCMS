using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class UserUpdateRequest
{
    [Required]
    public required Guid Id { get; set; }
    [Required]
    public required string Email { get; set; }

    public string PhoneNumber { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
}
