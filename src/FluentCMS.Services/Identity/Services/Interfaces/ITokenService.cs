namespace uBeac.Identity;

public interface ITokenService<TUserKey, TUser> 
    where TUserKey : IEquatable<TUserKey>
    where TUser : User<TUserKey>
{
    Task<TokenResult> Generate(TUser user);
    Task<TUserKey> Validate(string accessToken);
    Task<TUserKey> ValidateExpiredToken(string accessToken);
}

public interface ITokenService<TUser> : ITokenService<Guid, TUser>
    where TUser : User
{
}