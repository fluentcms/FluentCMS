using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public interface IUserService : IAutoRegisterService
{
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<bool> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
}

public class UserService(IGlobalSettingsRepository globalSettingsRepository, UserManager<User> userManager, IPermissionManager permissionManager, IMessagePublisher messagePublisher) : IUserService
{

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        // Only super admins can create users
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        var newUser = await GetById(user.Id, cancellationToken);

        await messagePublisher.Publish(new Message<User>(ActionNames.UserCreated, newUser), cancellationToken);

        return newUser;
    }

    public async Task<bool> ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default)
    {
        // Only super admins can change password for another user
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();

        await messagePublisher.Publish(new Message<User>(ActionNames.UserPasswordChanged, user), cancellationToken);

        return true;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        // Only super admins can update users
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var prevUser = await GetById(user.Id, cancellationToken);
        user = Merge(prevUser, user);
        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        var updated = await GetById(user.Id, cancellationToken);

        await messagePublisher.Publish(new Message<User>(ActionNames.UserUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var user = await userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);

        var globalSettings = await globalSettingsRepository.Get(cancellationToken) ??
            throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        if (globalSettings.SuperAdmins.Contains(user.UserName))
            throw new AppException(ExceptionCodes.UserSuperAdminCanNotBeDeleted);

        var userRemoveResult = await userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        await messagePublisher.Publish(new Message<User>(ActionNames.UserDeleted, user), cancellationToken);

        return true;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return [.. userManager.Users];
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return await userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);
    }

    private static T Merge<T>(T target, T source)
    {
        var type = typeof(T);
        var properties = type.GetProperties();
        foreach (var property in properties)
        {
            // ignore if null
            if (property.GetValue(source) == null) continue;
            property.SetValue(target, property.GetValue(source));
        }
        return target;
    }

}
