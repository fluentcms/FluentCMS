using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Components.Accounts;

// The account details the first-run setup and the users screen both ask for, bound from the posted form.
public sealed class NewUserForm
{
    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }

    public NewUser ToNewUser() => new(Email!.Trim(), DisplayName!.Trim(), Password!);
}

// What is wrong with a new account, by field, plus anything that belongs to no field.
public sealed class NewUserErrors
{
    private readonly Dictionary<string, string> _fields = [];

    public string? Form { get; private set; }

    public bool Any => _fields.Count > 0 || Form is not null;

    public string? For(string field) => _fields.GetValueOrDefault(field);

    public void Validate(NewUserForm form, IStringLocalizer localizer)
    {
        Require(nameof(NewUserForm.DisplayName), form.DisplayName, localizer["Enter a name."]);
        Require(nameof(NewUserForm.Email), form.Email, localizer["Enter an email address."]);
        Require(nameof(NewUserForm.Password), form.Password, localizer["Enter a password."]);
        if (form.Password != form.ConfirmPassword)
        {
            _fields[nameof(NewUserForm.ConfirmPassword)] = localizer["The passwords do not match."];
        }
    }

    // Identity reports problems by code: the code is the resource key, and Identity's own message the text until a resource exists.
    public void Report(IEnumerable<Error> errors, IStringLocalizer localizer)
    {
        foreach (var error in errors)
        {
            var localized = localizer[error.Code];
            var message = localized.ResourceNotFound ? error.Message : localized.Value;
            var field = error.Code switch
            {
                "InvalidEmail" or "DuplicateEmail" or "InvalidUserName" or "DuplicateUserName" => nameof(NewUserForm.Email),
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => nameof(NewUserForm.Password),
                _ => null,
            };

            if (field is null)
            {
                Form = Form is null ? message : Form + " " + message;
            }
            else
            {
                _fields[field] = _fields.TryGetValue(field, out var existing) ? existing + " " + message : message;
            }
        }
    }

    private void Require(string field, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _fields[field] = message;
        }
    }
}
