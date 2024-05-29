﻿namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ResetPasswordViewPlugin
{
    public const string FORM_NAME = "ResetPasswordForm";

    [SupplyParameterFromQuery(Name = nameof(Email))]
    public string Email { get; set; } = default!;

    [SupplyParameterFromQuery(Name = nameof(Token))]
    public string Token { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserValidatePasswordResetTokenRequest Model { get; set; } = new();

    protected override void OnInitialized()
    {
        if (string.IsNullOrEmpty(Email))
        {
            throw new ArgumentNullException(nameof(Email));
        }

        if (string.IsNullOrEmpty(Token))
        {
            throw new ArgumentNullException(nameof(Token));
        }
        Model.Email = Email;
        Model.Token = Token;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().ValidatePasswordResetTokenAsync(Model);
        NavigateTo("/auth/login");
    }
}
