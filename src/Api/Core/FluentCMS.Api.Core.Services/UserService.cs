namespace FluentCMS.Api.Core.Services;

public interface IUserService
{
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Add(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
}

public class UserService(IGlobalSettingsRepository globalSettingsRepository, UserManager<User> userManager, IPermissionManager permissionManager, IEventPublisher eventPublisher) : IUserService
{
    public async Task<User> Add(User user, string password, CancellationToken cancellationToken = default)
    {
        // Only super admins can create users
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        var newUser = await GetById(user.Id, cancellationToken);

        await eventPublisher.Publish(new UserAddedEvent(newUser), cancellationToken);

        return newUser;
    }

    public async Task<User> ChangePassword(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        // Only super admins can change password for another user
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var user = await userManager.FindByIdAsync(userId.ToString()) ??
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();

        await eventPublisher.Publish(new UserChangedPasswordEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        // Only super admins can update users
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var prevUser = await GetById(user.Id, cancellationToken);
        user = Merge(prevUser, user);
        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        var updated = await GetById(user.Id, cancellationToken);

        await eventPublisher.Publish(new UserUpdatedEvent(updated), cancellationToken);

        return updated;
    }

    public async Task<User> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var user = await userManager.FindByIdAsync(id.ToString())
            ?? throw new EnhancedException(ExceptionCodes.UserNotFound);

        var globalSettings = await globalSettingsRepository.Get(cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.GlobalSettingsNotFound);

        if (globalSettings.SuperAdmins.Contains(user.UserName!))
            throw new EnhancedException(ExceptionCodes.UserSuperAdminCanNotBeDeleted);

        var userRemoveResult = await userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        await eventPublisher.Publish(new UserRemovedEvent(user), cancellationToken);

        return user;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        return [.. userManager.Users];
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        return await userManager.FindByIdAsync(id.ToString())
            ?? throw new EnhancedException(ExceptionCodes.UserNotFound);
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
