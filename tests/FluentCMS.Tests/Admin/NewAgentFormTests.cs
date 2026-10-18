using FluentCMS.Admin.Components.Agents;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Tests.Admin;

public sealed class NewAgentFormTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly IStringLocalizer Localizer = new KeyLocalizer();

    [Fact]
    public void The_defaults_describe_a_read_only_agent_for_every_type_with_the_default_caps()
    {
        var errors = new NewAgentErrors();

        var credential = errors.Read(Named("catalog-sync"), Now, Localizer)!;

        Assert.False(errors.Any);
        Assert.Equal("catalog-sync", credential.DisplayName);
        Assert.Equal(Now.AddDays(90), credential.ExpiresAt);
        Assert.Equal([Site.Default.Id], credential.Scope.Sites);
        Assert.Null(credential.Scope.Types);
        Assert.Null(credential.Scope.Fields);
        Assert.Equal([AgentOperation.Read], credential.Scope.Operations);
        Assert.Equal(AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 200, 20).Value, credential.Caps);
    }

    [Fact]
    public void Lists_are_split_on_commas_and_operations_follow_the_ticked_boxes()
    {
        var form = Named(" price-check ");
        form.Types = " product ,category,, ";
        form.Fields = "product.price, product.currency";
        form.Read = false;
        form.Update = true;
        form.Delete = true;
        form.ValidForDays = "7";
        form.LlmSpendPerDay = "2.50";

        var credential = new NewAgentErrors().Read(form, Now, Localizer)!;

        Assert.Equal("price-check", credential.DisplayName);
        Assert.Equal(Now.AddDays(7), credential.ExpiresAt);
        Assert.Equal(["product", "category"], credential.Scope.Types);
        Assert.Equal(["product.price", "product.currency"], credential.Scope.Fields);
        Assert.Equal([AgentOperation.Update, AgentOperation.Delete], credential.Scope.Operations);
        Assert.Equal(2.50m, credential.Caps.LlmSpendPerPeriod);
    }

    [Fact]
    public void Every_problem_is_reported_next_to_its_field_at_once()
    {
        var form = new NewAgentForm
        {
            DisplayName = " ",
            ValidForDays = "3651",
            Types = "Product",
            Fields = "price",
            CallsPerHour = "0",
            LlmSpendPerDay = "-1",
            ItemsPerChangeSet = "ten",
            ChangeSetsPerDay = "1.5",
        };
        var errors = new NewAgentErrors();

        Assert.Null(errors.Read(form, Now, Localizer));

        Assert.Equal("Enter a name.", errors.For(nameof(NewAgentForm.DisplayName)));
        Assert.Equal("Enter a whole number from 1 to 3650.", errors.For(nameof(NewAgentForm.ValidForDays)));
        Assert.Equal(AgentCredentialErrorCodes.ScopeTypeInvalid, errors.For(nameof(NewAgentForm.Types)));
        Assert.Equal(AgentCredentialErrorCodes.ScopeFieldInvalid, errors.For(nameof(NewAgentForm.Fields)));
        Assert.Equal(AgentCredentialErrorCodes.ScopeOperationsMissing, errors.For(NewAgentErrors.Operations));
        Assert.Equal("Enter a whole number of 1 or more.", errors.For(nameof(NewAgentForm.CallsPerHour)));
        Assert.StartsWith("Enter an amount in US dollars", errors.For(nameof(NewAgentForm.LlmSpendPerDay)));
        Assert.Equal("Enter a whole number of 1 or more.", errors.For(nameof(NewAgentForm.ItemsPerChangeSet)));
        Assert.Equal("Enter a whole number of 1 or more.", errors.For(nameof(NewAgentForm.ChangeSetsPerDay)));
        Assert.Null(errors.Form);
    }

    [Fact]
    public void Errors_from_core_go_to_their_field_or_above_the_form()
    {
        var errors = new NewAgentErrors();

        errors.Report(
            [
                new Error(AgentCredentialErrorCodes.DisplayNameTooLong, "Too long."),
                new Error(AgentCredentialErrorCodes.ScopeFieldOutsideTypes, "Outside."),
                new Error(AgentCredentialErrorCodes.ScopeSiteUnknown, "No such site."),
            ],
            new MissingLocalizer());

        Assert.Equal("Too long.", errors.For(nameof(NewAgentForm.DisplayName)));
        Assert.Equal("Outside.", errors.For(nameof(NewAgentForm.Fields)));
        Assert.Equal("No such site.", errors.Form);
    }

    private static NewAgentForm Named(string displayName)
    {
        var form = NewAgentForm.Defaults();
        form.DisplayName = displayName;
        return form;
    }

    // Returns the key itself, formatted, as the admin's test context does.
    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    // Has no resources, as the host runs today, so Core's own message is shown.
    private sealed class MissingLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
