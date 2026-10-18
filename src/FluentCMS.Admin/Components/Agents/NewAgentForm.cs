using System.Globalization;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Components.Agents;

// A new credential as the screen asks for it, bound from the posted form. Numbers stay text until they are read, so a typo is
// reported next to its field instead of failing the binding. The form offers the hour and the day as periods; the model takes any.
public sealed class NewAgentForm
{
    public const int MaxValidForDays = 3650;

    public string? DisplayName { get; set; }

    public string? ValidForDays { get; set; }

    public string? Types { get; set; }

    public string? Fields { get; set; }

    public bool Read { get; set; }

    public bool Create { get; set; }

    public bool Update { get; set; }

    public bool Delete { get; set; }

    public string? CallsPerHour { get; set; }

    public string? LlmSpendPerDay { get; set; }

    public string? ItemsPerChangeSet { get; set; }

    public string? ChangeSetsPerDay { get; set; }

    // Only for the first render: a posted form starts from nothing, so a box left unticked stays unticked.
    public static NewAgentForm Defaults() => new()
    {
        ValidForDays = "90",
        Read = true,
        CallsPerHour = "600",
        LlmSpendPerDay = "5",
        ItemsPerChangeSet = "200",
        ChangeSetsPerDay = "20",
    };
}

// What is wrong with a new credential, by field, plus anything that belongs to no field.
public sealed class NewAgentErrors
{
    public const string Operations = "Operations";

    private readonly Dictionary<string, string> _fields = [];

    public string? Form { get; private set; }

    public bool Any => _fields.Count > 0 || Form is not null;

    public string? For(string field) => _fields.GetValueOrDefault(field);

    // The credential the form describes, checked with Core's own rules; null while anything is wrong. Until multi-site (MST-01)
    // the scope always holds the one site.
    public NewAgentCredential? Read(NewAgentForm form, DateTimeOffset now, IStringLocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(form.DisplayName))
        {
            Add(nameof(NewAgentForm.DisplayName), localizer["Enter a name."]);
        }

        var days = WholeNumber(nameof(NewAgentForm.ValidForDays), form.ValidForDays, NewAgentForm.MaxValidForDays, localizer);
        var calls = WholeNumber(nameof(NewAgentForm.CallsPerHour), form.CallsPerHour, int.MaxValue, localizer);
        var items = WholeNumber(nameof(NewAgentForm.ItemsPerChangeSet), form.ItemsPerChangeSet, int.MaxValue, localizer);
        var sets = WholeNumber(nameof(NewAgentForm.ChangeSetsPerDay), form.ChangeSetsPerDay, int.MaxValue, localizer);
        var spend = Amount(nameof(NewAgentForm.LlmSpendPerDay), form.LlmSpendPerDay, localizer);

        var scope = AgentScope.Create([Site.Default.Id], List(form.Types), List(form.Fields), OperationsOf(form));
        if (!scope.IsSuccess)
        {
            Report(scope.Errors, localizer);
        }

        if (calls is { } c && spend is { } s && items is { } i && sets is { } d)
        {
            var caps = AgentCaps.Create(c, TimeSpan.FromHours(1), s, TimeSpan.FromDays(1), i, d);
            if (!caps.IsSuccess)
            {
                Report(caps.Errors, localizer);
            }
            else if (!Any)
            {
                return new NewAgentCredential(form.DisplayName!.Trim(), now.AddDays(days!.Value), scope.Value, caps.Value);
            }
        }

        return null;
    }

    // Core reports problems by code: the code is the resource key, and Core's own message the text until a resource exists.
    public void Report(IEnumerable<Error> errors, IStringLocalizer localizer)
    {
        foreach (var error in errors)
        {
            var localized = localizer[error.Code];
            var message = localized.ResourceNotFound ? error.Message : localized.Value;
            var field = error.Code switch
            {
                AgentCredentialErrorCodes.DisplayNameMissing or AgentCredentialErrorCodes.DisplayNameTooLong => nameof(NewAgentForm.DisplayName),
                AgentCredentialErrorCodes.ExpiryNotInFuture => nameof(NewAgentForm.ValidForDays),
                AgentCredentialErrorCodes.ScopeTypesEmpty or AgentCredentialErrorCodes.ScopeTypeInvalid => nameof(NewAgentForm.Types),
                AgentCredentialErrorCodes.ScopeFieldsEmpty
                    or AgentCredentialErrorCodes.ScopeFieldInvalid
                    or AgentCredentialErrorCodes.ScopeFieldOutsideTypes => nameof(NewAgentForm.Fields),
                AgentCredentialErrorCodes.ScopeOperationsMissing or AgentCredentialErrorCodes.ScopeOperationInvalid => Operations,
                _ => null,
            };

            if (field is null)
            {
                Form = Form is null ? message : Form + " " + message;
            }
            else
            {
                Add(field, message);
            }
        }
    }

    // Comma-separated names, or null for all of them when the box is left empty.
    private static List<string>? List(string? value)
    {
        var names = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return names.Length == 0 ? null : [.. names];
    }

    private static List<AgentOperation> OperationsOf(NewAgentForm form)
    {
        return new (bool Ticked, AgentOperation Operation)[]
        {
            (form.Read, AgentOperation.Read),
            (form.Create, AgentOperation.Create),
            (form.Update, AgentOperation.Update),
            (form.Delete, AgentOperation.Delete),
        }.Where(option => option.Ticked).Select(option => option.Operation).ToList();
    }

    private int? WholeNumber(string field, string? value, int max, IStringLocalizer localizer)
    {
        if (int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= max)
        {
            return number;
        }

        Add(field, max == int.MaxValue
            ? localizer["Enter a whole number of 1 or more."]
            : localizer["Enter a whole number from 1 to {0}.", max]);
        return null;
    }

    private decimal? Amount(string field, string? value, IStringLocalizer localizer)
    {
        if (decimal.TryParse(value?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }

        Add(field, localizer["Enter an amount in US dollars, such as 5 or 2.50; 0 allows no model spend."]);
        return null;
    }

    private void Add(string field, string message)
    {
        _fields[field] = _fields.TryGetValue(field, out var existing) ? existing + " " + message : message;
    }
}
