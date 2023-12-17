using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing user accounts in the FluentCMS system.
/// Provides actions for user registration, login, and password changes.
/// </summary>
public class AccountController(IMapper mapper, IUserService userService) : BaseController
{
    /// <summary>
    /// Registers a new user in the system.
    /// </summary>
    /// <param name="request">The user registration request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The details of the newly created user.</returns>
    [HttpPost]
    public async Task<IApiResult<UserDetailResponse>> Register(UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var result = mapper.Map<UserDetailResponse>(newUser);
        return new ApiResult<UserDetailResponse>(result);
    }

    /// <summary>
    /// Authenticates a user and provides a token for accessing secured resources.
    /// </summary>
    /// <param name="request">The user login request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The login response including the user token and role information.</returns>
    [HttpPost]
    public async Task<IApiResult<UserLoginResponse>> Login(UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.Login(request.Username, request.Password, cancellationToken);
        var userToken = await userService.GetToken(user, cancellationToken);
        return new ApiResult<UserLoginResponse>(new UserLoginResponse
        {
            Token = userToken.AccessToken,
            RoleIds = user.RoleIds,
            UserId = user.Id
        });
    }

    /// <summary>
    /// Changes the password for a user.
    /// </summary>
    /// <param name="request">The user password change request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A boolean result indicating the success of the password change operation.</returns>
    [HttpPost]
    public async Task<IApiResult<bool>> ChangePassword(UserChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(request.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return new ApiResult<bool>(true);
    }

}
