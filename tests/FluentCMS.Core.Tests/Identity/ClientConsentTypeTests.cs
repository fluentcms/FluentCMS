using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using Shouldly;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Tests.Identity;

public sealed class ClientConsentTypeTests
{
    [Fact]
    public void AClientOfTheHostSignsInWithoutAConsentPage()
    {
        var descriptor = ClientRegistrationService.Describe("admin", new ClientRegistration { RedirectUris = { "https://admin.example/callback" } });

        descriptor.ConsentType.ShouldBe(ConsentTypes.Implicit);
    }

    [Fact]
    public void AThirdPartyClientAsksForConsent()
    {
        var descriptor = ClientRegistrationService.Describe("agent", new ClientRegistration { RedirectUris = { "https://agent.example/callback" }, RequireConsent = true });

        descriptor.ConsentType.ShouldBe(ConsentTypes.Explicit);
    }
}
