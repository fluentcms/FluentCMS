using AutoMapper;
using FluentCMS.Api.Controllers;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Identity;
using FluentCMS.Api.Models.Users;
using FluentCMS.Entities.Users;
using FluentCMS.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Api.Identity;
[Route("api/[controller]/[action]")]
public class IdentityController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly FluentUserManager _userManager;
    private readonly FluentSignInManager _signInManager;
    private readonly IMapper _mapper;

    public IdentityController(IUserService userService, FluentUserManager userManager, FluentSignInManager fluentSignInManager, IMapper mapper)
    {
        _userService = userService;
        _userManager = userManager;
        _signInManager = fluentSignInManager;
        _mapper = mapper;
    }
    [HttpPost]
    [Produces<IApiResult<TokenResponse>>]
    public async Task<IActionResult> Login([FromBody] FluentLoginRequest request)
    {
        try
        {
            var user = await _userService.GetByUsername(request.UserName);
            var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, true);
            if (signInResult.Succeeded)
            {

                var claimsPrincipal = await _signInManager.CreateUserPrincipalAsync(user);
                var accessToken = _signInManager.CreateJwtToken(claimsPrincipal);
                var refreshToken = await _userManager.GenerateUserTokenAsync(user, RefreshTokenProviderOptions.RefreshTokenProviderName, "refresh-token");
                return Ok(new ApiResult<TokenResponse>(new TokenResponse(accessToken, refreshToken)));
            }
            return Unauthorized();
        }
        catch (ApplicationException ex)
        {

            return BadRequest(ex.Message);
        }
    }
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] FluentRegisterRequest request)
    {
        var user = new User() { Username = request.Username };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (result.Succeeded) return Ok();
        return BadRequest(result.Errors);
    }
    [HttpPost]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Produces<IApiResult<TokenResponse>>]
    public async Task<IActionResult> Refresh([FromBody] FluentRefreshRequest request)
    {
        var user = await _userManager.GetUserAsync(HttpContext.User);
        if (await _signInManager.ValidateSecurityStampAsync(HttpContext.User) == null)
        {
            return Challenge();
        }
        if (await _userManager.VerifyUserTokenAsync(user, RefreshTokenProviderOptions.RefreshTokenProviderName, "refresh-token", request.RefreshToken))
        {
            await _userManager.UpdateSecurityStampAsync(user); // invalidate previous tokens
            var claimsPrincipal = await _signInManager.CreateUserPrincipalAsync(user);
            var accessToken = _signInManager.CreateJwtToken(claimsPrincipal);
            var refreshToken = await _userManager.GenerateUserTokenAsync(user, RefreshTokenProviderOptions.RefreshTokenProviderName, "refresh-token");
            return Ok(new ApiResult<TokenResponse>(new TokenResponse(accessToken, refreshToken)));
        }
        return Challenge();

    }
    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IApiResult<UserResponse>> GetUserInfo()
    {
        return new ApiResult<UserResponse>(_mapper.Map<UserResponse>(await _userManager.GetUserAsync(HttpContext.User)));
    }
}
