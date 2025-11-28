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



public class PageDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; }
    public int Order { get; set; }

    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? OgType { get; set; }

    public string? Head { get; set; }
}

public class PageAddRequest
{
    public string Title { get; set; } = default!;
    public string? Slug { get; set; }
    public int Order { get; set; }

    public Guid? ParentId { get; set; }
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? OgType { get; set; }

    public string? Head { get; set; }
}

public class PageUpdateRequest
{
    public string Title { get; set; } = default!;
    public string? Slug { get; set; }
    public int Order { get; set; }

    public Guid? ParentId { get; set; }
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? OgType { get; set; }

    public string? Head { get; set; }
}


public class LayoutDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}

public class LayoutAddRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}

public class LayoutUpdateRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Head { get; set; }
    public string Body { get; set; }
}


#region Folder

public class FolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; }
    public List<FileDto> Files { get; set; } = [];
    public List<FolderDto> Folders { get; set; } = [];
    public FolderDto? ParentFolder { get; set; }
}

public class FolderAddRequest
{
    public string Name { get; set; }
    public Guid ParentId { get; set; }
}

public class FolderRenameRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class FolderMoveRequest
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
}


#endregion

#region File

public class FileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string NormalizedName { get; set; } = default!;
    public Guid FolderId { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
}

// TODO: upload..
public class FileAddRequest
{
    public string Name { get; set; }
}

public class FileMoveRequest
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
}

public class FileRenameRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}


#endregion

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

public class SiteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];

    public Guid? LayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? RobotsTxt { get; set; }
    public string? GoogleTagsId { get; set; }
    public string? OgType { get; set; }
    public string? Head { get; set; }    
}

public class SiteAddRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid? LayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? RobotsTxt { get; set; }
    public string? GoogleTagsId { get; set; }
    public string? OgType { get; set; }
    public string? Head { get; set; }    
}

public class SiteUpdateRequest
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public List<string> Urls { get; set; } = [];

    public Guid? LayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? RobotsTxt { get; set; }
    public string? GoogleTagsId { get; set; }
    public string? OgType { get; set; }
    public string? Head { get; set; }    
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
