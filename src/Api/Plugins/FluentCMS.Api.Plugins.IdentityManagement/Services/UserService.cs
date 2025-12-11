namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IUserService
{
    Task<User> Add(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Update(Guid id, UserUpdateRequest user, CancellationToken cancellationToken = default);
    Task<User> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
}

internal class UserService(UserManager<User> userManager, IEventPublisher eventPublisher, IUserRepository userRepository) : IUserService
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

    public async Task<User> Update(Guid id, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        // Get the tracked identity user (with SecurityStamp, ConcurrencyStamp, etc.)
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw new Exception("User not found");

        // Update only the fields you expose in the API
        user.Email = request.Email;
        user.Description = request.Description;
        user.Suspended = request.Suspended;
        user.IsSuperAdmin = request.IsSuperAdmin;
        user.EmailConfirmed = request.EmailConfirmed;
        user.LockoutEnabled = request.Locked;

        // Identity will validate security stamp, concurrency stamp, etc.
        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        await eventPublisher.Publish(new UserUpdatedEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetById(id, cancellationToken);

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
