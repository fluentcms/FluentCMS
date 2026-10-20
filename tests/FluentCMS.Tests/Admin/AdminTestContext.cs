using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Tests.Admin;

// Renders admin components with the services the shell registers; strings come back as their keys.
internal sealed class AdminTestContext : BunitContext
{
    public AdminTestContext()
    {
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(KeyLocalizer<>));
        Services.AddFluentCmsAdmin();
        State = this.AddBunitPersistentComponentState();
    }

    // What prerendered components keep for the circuit, as the page carries it.
    public BunitPersistentComponentState State { get; }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
