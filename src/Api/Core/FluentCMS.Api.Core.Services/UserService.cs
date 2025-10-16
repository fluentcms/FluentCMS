namespace FluentCMS.Api.Core.Services;

public interface IUserService
{
    Task<User> Add(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
}

internal class UserService(IGlobalSettingsRepository globalSettingsRepository, UserManager<User> userManager, IEventPublisher eventPublisher, IUserRepository userRepository) : IUserService
{
    public async Task<User> Add(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new UserAddedEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> ChangePassword(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetById(userId, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();

        await eventPublisher.Publish(new UserChangedPasswordEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        await eventPublisher.Publish(new UserUpdatedEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetById(id, cancellationToken);

        var globalSettings = await globalSettingsRepository.Get(cancellationToken);

        if (globalSettings.SuperAdmins.Contains(user.UserName!))
            throw new EnhancedException(ExceptionCodes.UserSuperAdminCanNotBeDeleted);

        var userRemoveResult = await userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        await eventPublisher.Publish(new UserRemovedEvent(user), cancellationToken);

        return user;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        return [.. await userRepository.GetAll(cancellationToken)];
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await userRepository.GetById(id, cancellationToken);
    }
}
