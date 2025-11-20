using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Admin.Api.ApiModels;



public class ApiResponseBase
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? MessageCode { get; set; }
    public List<ApiError>? Errors { get; set; }
    public DateTime Timestamp { get; set; }
    public string? TraceId { get; set; }
    public int StatusCode { get; set; }
    public string? SessionId { get; set; }
    public string? UniqueId { get; set; }
    public double Duration { get; set; }
}


public class ApiResponse<T> : ApiResponseBase
{
    public T? Data { get; set; }
}

public class ListApiResponse<T> : ApiResponseBase
{
    public List<T>? Data { get; set; }
    public PaginationInfo? Pagination { get; set; }
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}


public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Description { get; set; }
    public bool IsSuperAdmin { get; set; }
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string PasswordChangedBy { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool Suspended { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
}

public class UserAddRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string Email { get; set; }
    public bool Suspended { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool Locked { get; set; }
    public string Description { get; set; }
}

public class LayoutDto
{
    public Guid? Id { get; set; } = null!;
}

public class AccountChangePasswordRequest
{
    public string UserName { get; set; } = null!;
    public string OldPassword { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
    public string ConfirmPassword { get; set; } = null!;
}

public class AccountConfirmEmailRequest
{
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Token { get; set; } = null!;
}

public class AccountForgotPasswordRequest
{
    public string Email { get; set; } = null!;
}

public class AccountLoginRequest
{
    public string UserName { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class AccountRegisterRequest
{
    public string Email { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string ConfirmPassword { get; set; } = null!;
}

public class AccountResendConfirmationRequest
{
    public string Email { get; set; } = null!;
}

public class AccountResetPasswordRequest
{
    public string Email { get; set; } = null!;
    public string Token { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
    public string ConfirmPassword { get; set; } = null!;
}

public class ApiError
{
    public string? Code { get; set; }
    public string? Description { get; set; }
}

public class LoginDto
{
    public string? Token { get; set; }
}

public class RoleAddRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public RoleTypes? Type { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoleTypes
{
    UserDefined,
    Administrators,
    Authenticated,
    Guest,
    AllUsers
}

public class RoleUpdateRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}

public class SiteAddRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = new List<string>();
}

public class SiteDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Urls { get; set; }
}

public class TodoCreateDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
}

public class TodoResponseDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }
}

public class TodoResponseDtoApiListResponse : ListApiResponse<TodoResponseDto>
{
}

public class TodoResponseDtoApiResponse : ApiResponse<TodoResponseDto>
{
}

public class TodoUpdateDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
}

public class UsageDetails
{
    public long? InputTokenCount { get; set; }
    public long? OutputTokenCount { get; set; }
    public long? TotalTokenCount { get; set; }
    public Dictionary<string, long>? AdditionalCounts { get; set; }
}

public class UserRolesUpdateRequest
{
    public List<Guid> RoleIds { get; set; } = new List<Guid>();
    public Guid UserId { get; set; }
}

public class UserUpdateRequest
{
    public string Email { get; set; } = null!;
    public bool Suspended { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool Locked { get; set; }
    public string? Description { get; set; }
}
