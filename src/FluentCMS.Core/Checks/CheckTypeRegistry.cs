using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Checks;

// Every check type registered through the extension point, by name. Built-ins and plugins arrive the same way (EXT-N1).
public sealed class CheckTypeRegistry
{
    private readonly Dictionary<string, ICheckType> _byName = new(StringComparer.Ordinal);

    public CheckTypeRegistry(IEnumerable<ICheckType> checkTypes)
    {
        ArgumentNullException.ThrowIfNull(checkTypes);

        // A name is stored in every check that uses it, so a bad or duplicate one stops the application from starting.
        foreach (var checkType in checkTypes)
        {
            if (!CheckNames.IsTypeName(checkType.Name))
            {
                throw new InvalidOperationException(
                    $"{checkType.GetType()} is named '{checkType.Name}', which is not a valid check type name.");
            }

            if (!Enum.IsDefined(checkType.Kind))
            {
                throw new InvalidOperationException($"{checkType.GetType()} has the kind '{checkType.Kind}', which is not a check kind.");
            }

            if (!_byName.TryAdd(checkType.Name, checkType))
            {
                throw new InvalidOperationException(
                    $"{checkType.GetType()} and {_byName[checkType.Name].GetType()} are both named '{checkType.Name}'.");
            }
        }
    }

    public IReadOnlyList<string> Names => _byName.Keys.Order(StringComparer.Ordinal).ToList();

    public ICheckType? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    public IReadOnlyList<Error> ValidateSettings(string type, JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (Find(type) is not { } checkType)
        {
            return [TypeUnknown(type)];
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            return [new Error(CheckErrorCodes.SettingInvalid, "The settings must be a JSON object.")];
        }

        return checkType.ValidateSettings(settings);
    }

    // Content must never pass because its check could not run, so a type that is not installed or that throws gives needs review.
    public async Task<CheckOutcome> EvaluateAsync(string type, CheckInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(input);

        if (Find(type) is not { } checkType)
        {
            return CheckOutcome.NeedsReview($"The check type '{type}' is not installed, so the check could not run.");
        }

        try
        {
            return await checkType.EvaluateAsync(input, cancellationToken)
                ?? CheckOutcome.NeedsReview($"The check type '{type}' returned no outcome.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return CheckOutcome.NeedsReview($"The check type '{type}' failed while running: {exception.Message}");
        }
    }

    private Error TypeUnknown(string type)
    {
        return new Error(
            CheckErrorCodes.CheckTypeUnknown,
            _byName.Count == 0
                ? $"There is no check type '{type}'; no check types are installed."
                : $"There is no check type '{type}'; the known types are {string.Join(", ", Names)}.");
    }
}
