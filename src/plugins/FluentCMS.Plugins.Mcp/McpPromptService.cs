using System.Text.RegularExpressions;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

public sealed record McpPromptArgumentInfo(string? Name, string? Description, bool? Required);

public sealed record McpPromptRequest(string? Name, string? Title, string? Description, IReadOnlyList<McpPromptArgumentInfo>? Arguments, string? Template);

public sealed record McpPromptInfo(
    Guid Id,
    Guid SiteId,
    string Name,
    string? Title,
    string? Description,
    IReadOnlyList<McpPromptArgumentInfo> Arguments,
    string Template,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy);

// The stored prompts of the site of the current operation. Every call needs mcp.manage in that site; a prompt of another
// site is not found. A prompt is unique by name in its site, and takes no name that a provider of a plugin lists, since
// the server would serve only one of the two.
internal sealed partial class McpPromptService(
    PluginDbContext<McpPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    IEventBus events,
    IMcpProviders providers,
    IPluginState plugins,
    IServiceProvider services,
    TimeProvider time,
    ILogger<McpPromptService> logger)
{
    public const int MaxPrompts = 100;
    public const int MaxArguments = 20;
    public const int TemplateMaxLength = 16000;

    public async Task<IReadOnlyList<McpPromptInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var prompts = await database.Set<McpPrompt>().AsNoTracking().Where(prompt => prompt.SiteId == site.Value).ToListAsync(cancellationToken);
        return [.. prompts.OrderBy(prompt => prompt.Name, StringComparer.Ordinal).Select(Info)];
    }

    public async Task<McpPromptInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        return Info(await FindAsync(site, id, tracked: false, cancellationToken));
    }

    public async Task<McpPromptInfo> CreateAsync(McpPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await RequireAsync(cancellationToken);
        var valid = Validate(request);
        if (await database.Set<McpPrompt>().CountAsync(prompt => prompt.SiteId == site.Value, cancellationToken) >= MaxPrompts)
        {
            throw Invalid("", $"A site has at most {MaxPrompts} stored prompts.");
        }

        var prompt = new McpPrompt { Id = Guid.CreateVersion7(), SiteId = site.Value };
        database.Add(prompt);
        await SaveAsync(site, prompt, valid, cancellationToken);
        McpLog.PromptChanged(logger, "created", prompt.Name, site.Value);
        await events.PublishAsync(new McpPromptCreatedEvent(prompt.Id, prompt.Name), cancellationToken);
        return Info(prompt);
    }

    // The whole prompt.
    public async Task<McpPromptInfo> UpdateAsync(Guid id, McpPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await RequireAsync(cancellationToken);
        var prompt = await FindAsync(site, id, tracked: true, cancellationToken);
        await SaveAsync(site, prompt, Validate(request), cancellationToken);
        McpLog.PromptChanged(logger, "updated", prompt.Name, site.Value);
        await events.PublishAsync(new McpPromptUpdatedEvent(prompt.Id, prompt.Name), cancellationToken);
        return Info(prompt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var prompt = await FindAsync(site, id, tracked: false, cancellationToken);
        if (await database.Set<McpPrompt>().Where(item => item.Id == prompt.Id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw NotFound(id);
        }

        McpLog.PromptChanged(logger, "deleted", prompt.Name, site.Value);
        await events.PublishAsync(new McpPromptDeletedEvent(prompt.Id, prompt.Name), cancellationToken);
    }

    // The stored prompts of a deleted site, for the system rather than a caller, so nothing is checked and no event is
    // raised. A second run finds nothing.
    public Task<int> RemoveAllAsync(Guid siteId, CancellationToken cancellationToken) =>
        database.Set<McpPrompt>().Where(prompt => prompt.SiteId == siteId).ExecuteDeleteAsync(cancellationToken);

    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The stored prompts of a site are managed inside an operation, which names the site.");

    private async Task<SiteId> RequireAsync(CancellationToken cancellationToken)
    {
        var site = Operation().Site;
        await permissions.RequireAsync(McpPermissions.Manage, site, cancellationToken);
        return site;
    }

    private async Task<McpPrompt> FindAsync(SiteId site, Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var prompts = tracked ? database.Set<McpPrompt>() : database.Set<McpPrompt>().AsNoTracking();
        return await prompts.FirstOrDefaultAsync(prompt => prompt.Id == id && prompt.SiteId == site.Value, cancellationToken) ?? throw NotFound(id);
    }

    // Two writers that both found the name free meet at the unique index.
    private async Task SaveAsync(SiteId site, McpPrompt prompt, ValidRequest valid, CancellationToken cancellationToken)
    {
        if (await ProviderListsAsync(valid.Name, cancellationToken))
        {
            throw new PluginException(McpErrorCodes.PromptNameReserved, $"A plugin offers a prompt named '{valid.Name}', so a stored prompt cannot take that name.");
        }

        prompt.Name = valid.Name;
        prompt.Title = valid.Title;
        prompt.Description = valid.Description;
        prompt.Arguments = McpStoredPrompts.Json(valid.Arguments);
        prompt.Template = valid.Template;
        prompt.UpdatedAt = time.GetUtcNow();
        prompt.UpdatedBy = Operation().AuditPrincipal.Id;

        if (await NameTakenAsync(site, prompt, cancellationToken))
        {
            throw NameTaken(prompt.Name);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await NameTakenAsync(site, prompt, CancellationToken.None))
            {
                throw NameTaken(prompt.Name);
            }

            throw;
        }
    }

    private Task<bool> NameTakenAsync(SiteId site, McpPrompt prompt, CancellationToken cancellationToken) =>
        database.Set<McpPrompt>().AsNoTracking().AnyAsync(item => item.SiteId == site.Value && item.Name == prompt.Name && item.Id != prompt.Id, cancellationToken);

    // Whether a provider of a plugin that is enabled in the site lists a prompt of the name to the caller. A provider that
    // fails to list is passed over, as the server passes over it when it lists.
    private async Task<bool> ProviderListsAsync(string name, CancellationToken cancellationToken)
    {
        var operation = Operation();
        var request = new McpListRequest(operation);
        foreach (var registration in providers.Prompts)
        {
            if (registration.Provider == typeof(McpStoredPromptProvider) || !await plugins.IsEnabledAsync(registration.Plugin, operation.Site, cancellationToken))
            {
                continue;
            }

            IReadOnlyList<McpPromptDescriptor> listed;
            try
            {
                listed = await ((IMcpPromptProvider)services.GetRequiredService(registration.Provider)).ListPromptsAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                McpLog.ListFailed(logger, exception, registration.Provider.Name, registration.Plugin);
                continue;
            }

            if (listed.Any(prompt => string.Equals(prompt.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static ValidRequest Validate(McpPromptRequest request)
    {
        List<ValidationError> errors = [];
        var name = request.Name?.Trim();
        if (name is null || !PromptName().IsMatch(name))
        {
            errors.Add(new("name", McpErrorCodes.InvalidPrompt, $"A name is required: 1 to {McpModel.NameMaxLength} lower case letters, digits, underscores, hyphens and dots, starting with a letter or a digit."));
        }

        var title = Optional(request.Title, "title", McpModel.TitleMaxLength, errors);
        var description = Optional(request.Description, "description", McpModel.DescriptionMaxLength, errors);

        var given = request.Arguments ?? [];
        if (given.Count > MaxArguments)
        {
            errors.Add(new("arguments", McpErrorCodes.InvalidPrompt, $"A prompt has at most {MaxArguments} arguments."));
        }

        List<McpStoredArgument> arguments = [];
        HashSet<string> declared = new(StringComparer.Ordinal);
        for (var index = 0; index < given.Count; index++)
        {
            var argument = given[index];
            if (!McpPromptTemplate.IsArgumentName(argument?.Name))
            {
                errors.Add(new($"arguments[{index}].name", McpErrorCodes.InvalidPrompt, "An argument needs a name: 1 to 64 letters, digits and underscores, starting with a letter or an underscore."));
                continue;
            }

            if (!declared.Add(argument!.Name!))
            {
                errors.Add(new($"arguments[{index}].name", McpErrorCodes.InvalidPrompt, $"The argument '{argument.Name}' is declared twice."));
                continue;
            }

            arguments.Add(new McpStoredArgument(argument.Name!, Optional(argument.Description, $"arguments[{index}].description", McpModel.DescriptionMaxLength, errors), argument.Required ?? false));
        }

        var template = request.Template;
        if (string.IsNullOrWhiteSpace(template) || template.Length > TemplateMaxLength)
        {
            errors.Add(new("template", McpErrorCodes.InvalidPrompt, $"A template is required, of at most {TemplateMaxLength} characters."));
        }
        else if (template.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
        {
            errors.Add(new("template", McpErrorCodes.InvalidPrompt, "The template holds a control character other than a line break or a tab."));
        }
        else
        {
            foreach (var named in McpPromptTemplate.Names(template).Where(named => !declared.Contains(named)))
            {
                errors.Add(new("template", McpErrorCodes.UndeclaredPromptArgument, $"The template names the argument '{named}', which the prompt does not declare."));
            }

            if (McpPromptTemplate.HasStrayOpening(template))
            {
                errors.Add(new("template", McpErrorCodes.InvalidPrompt, "The template has '{{' where no argument follows. An argument is written as {{name}}, with nothing else between the braces."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new ValidRequest(name!, title, description, arguments, template!);
    }

    // Null for a text that is missing or blank.
    private static string? Optional(string? given, string path, int maxLength, List<ValidationError> errors)
    {
        var text = given?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength || text.Any(char.IsControl))
        {
            errors.Add(new(path, McpErrorCodes.InvalidPrompt, $"The text has at most {maxLength} characters on one line."));
        }

        return text;
    }

    private static ValidationException Invalid(string path, string message) => new([new ValidationError(path, McpErrorCodes.InvalidPrompt, message)]);

    private static PluginException NotFound(Guid id) => new(McpErrorCodes.PromptNotFound, $"The site has no stored prompt {id:D}.");

    private static PluginException NameTaken(string name) => new(McpErrorCodes.PromptNameTaken, $"The site has a stored prompt named '{name}'.");

    private static McpPromptInfo Info(McpPrompt prompt) => new(
        prompt.Id,
        prompt.SiteId,
        prompt.Name,
        prompt.Title,
        prompt.Description,
        [.. (McpStoredPrompts.ArgumentsOf(prompt.Arguments) ?? []).Select(argument => new McpPromptArgumentInfo(argument.Name, argument.Description, argument.Required))],
        prompt.Template,
        prompt.UpdatedAt,
        prompt.UpdatedBy);

    // Lower case only, so that two names never differ by case alone, which the providers' databases would not agree on.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PromptName();

    private sealed record ValidRequest(string Name, string? Title, string? Description, List<McpStoredArgument> Arguments, string Template);
}
