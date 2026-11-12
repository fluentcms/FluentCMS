using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Core.Commands.AdminCommandSupport;

namespace FluentCMS.Core.Commands;

// `user create` makes an active administrator with a password, which is how a new instance gets its first one. It runs in
// the host only: over HTTP a user is invited, so that a key alone never sets a password.
internal sealed class UserCreateCommand : HostCommand
{
    public override string Name => "user create";

    public override string Description => "Creates an active admin user with a role; the password is asked for, or read from standard input.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Email, "The email address, which is also the user name.", "address"),
        new(Role, "The role the user gets, such as \"Super Admin\".", "name"),
        SiteOption,
        PasswordStdinOption,
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var email = Required(context, Email);
        var site = await SiteAsync(context, cancellationToken);
        var role = await RoleAsync(context, site, instanceWideToo: false, cancellationToken);
        var password = await PasswordAsync(context, cancellationToken);

        var user = await context.Services.GetRequiredService<UserAdministration>().CreateAsync(email, password, [role], cancellationToken);
        await context.Output.WriteLineAsync($"Created the user {user.UserName} ({user.Id:D}) with the role {context.Arguments.Value(Role)}.");
        return CommandExitCodes.Success;
    }
}

internal sealed class UserInviteCommand : HostCommand
{
    public override string Name => "user invite";

    public override string Description => "Sends an invitation by email; the person chooses a password on accepting it.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Email, "The email address to invite.", "address"),
        new(Role, "The role the user gets on accepting.", "name"),
        SiteOption,
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var email = Required(context, Email);
        var site = await SiteAsync(context, cancellationToken);
        var role = await RoleAsync(context, site, instanceWideToo: false, cancellationToken);
        var accept = Link(context, HostedPagePaths.AcceptInvitation);

        var sent = await context.Services.GetRequiredService<InvitationService>().InviteAsync(email, [role], accept, cancellationToken);
        await context.Output.WriteLineAsync($"Invited {sent.Email}; the invitation {sent.Id:D} expires at {Moment(sent.ExpiresAt)} UTC.");
        return CommandExitCodes.Success;
    }
}

// Sends the reset email, as the hosted pages do; with --password-stdin it sets the password instead, for an operator who
// cannot wait for an email.
internal sealed class UserResetPasswordCommand : HostCommand
{
    public override string Name => "user reset-password";

    public override string Description => "Sends a password reset email, or with --password-stdin sets the password read from standard input.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Email, "The email address of the user.", "address"),
        new(PasswordStdin, "Sets the password read from the first line of standard input instead of sending the email."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var email = Required(context, Email);
        var users = context.Services.GetRequiredService<UserAdministration>();
        var user = await users.FindByEmailAsync(email, cancellationToken)
            ?? throw new CommandException(CommandErrorCodes.UserNotFound, $"there is no user with the address {email}");
        if (user.DeactivatedAt is not null)
        {
            throw new CommandException(CommandErrorCodes.UserNotFound, $"the user {user.UserName} is deactivated; reactivate it first");
        }

        if (context.Arguments.Has(PasswordStdin))
        {
            await users.SetPasswordAsync(user.Id, await PasswordAsync(context, cancellationToken), cancellationToken);
            await context.Output.WriteLineAsync($"Set the password of {user.UserName}; its sessions are ended.");
            return CommandExitCodes.Success;
        }

        // Sent here rather than by a job, which may have no runner while a command runs; the command has told
        // whether the user exists already, so its time tells nothing more.
        var reset = Link(context, HostedPagePaths.ResetPassword);
        await context.Services.GetRequiredService<PasswordResetService>().SendAsync(email, reset, cancellationToken);
        await context.Output.WriteLineAsync($"Sent a password reset link to {user.Email}.");
        return CommandExitCodes.Success;
    }
}

internal sealed class UserAddRoleCommand : HostCommand
{
    public override string Name => "user add-role";

    public override string Description => "Gives a user a role.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Email, "The email address of the user.", "address"),
        new(Role, "The role to give.", "name"),
        SiteOption,
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var email = Required(context, Email);
        var site = await SiteAsync(context, cancellationToken);
        var role = await RoleAsync(context, site, instanceWideToo: false, cancellationToken);
        var users = context.Services.GetRequiredService<UserAdministration>();
        var user = await users.FindByEmailAsync(email, cancellationToken)
            ?? throw new CommandException(CommandErrorCodes.UserNotFound, $"there is no user with the address {email}");

        await users.AddRoleAsync(user.Id, role, cancellationToken);
        await context.Output.WriteLineAsync($"Gave {user.UserName} the role {context.Arguments.Value(Role)}.");
        return CommandExitCodes.Success;
    }
}
