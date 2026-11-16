using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AdminConfigurationReaderTests
{
    [Fact]
    public void EveryOptionalMemberHasItsDefault()
    {
        var configuration = Valid("""{ "apiBaseUrl": "https://cms.example.com" }""");

        configuration.ApiBaseUrl.ShouldBe(new Uri("https://cms.example.com/"));
        configuration.Authority.OriginalString.ShouldBe("https://cms.example.com");
        configuration.ClientId.ShouldBe("fluentcms-admin");
        configuration.Scopes.ShouldBe(["openid", "profile", "email", "offline_access", "management"]);
        configuration.Branding.Name.ShouldBe("FluentCMS");
        configuration.Branding.LogoUrl.ShouldBeNull();
        configuration.Branding.FaviconUrl.ShouldBeNull();
        configuration.Branding.AccentColor.ShouldBeNull();
        configuration.Cultures.Offered.ShouldBeNull();
        configuration.Cultures.Default.ShouldBe("en");
        configuration.PreviewOrigins.ShouldBeEmpty();
        configuration.PreviewEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ANullMemberCountsAsNotSet()
    {
        var configuration = Valid("""
            { "apiBaseUrl": "https://cms.example.com", "authority": null, "clientId": null, "scopes": null,
              "branding": { "name": null, "accentColor": null }, "cultures": null, "previewOrigins": null }
            """);

        configuration.Authority.OriginalString.ShouldBe("https://cms.example.com");
        configuration.ClientId.ShouldBe("fluentcms-admin");
        configuration.Branding.Name.ShouldBe("FluentCMS");
        configuration.PreviewEnabled.ShouldBeFalse();
    }

    [Fact]
    public void EveryMemberIsRead()
    {
        var configuration = Valid("""
            {
              "apiBaseUrl": "https://example.com/cms",
              "authority": "https://login.example.com/",
              "clientId": "team-admin",
              "scopes": ["openid", "management"],
              "branding": { "name": "Acme", "logoUrl": "img/logo.svg", "faviconUrl": "https://cdn.example.com/favicon.ico?v=2", "accentColor": "#0F6CBD" },
              "cultures": { "offered": ["en", "de-CH"], "default": "de-CH" },
              "previewOrigins": ["https://www.example.com/", "http://localhost:3000"]
            }
            """);

        configuration.ApiBaseUrl.ShouldBe(new Uri("https://example.com/cms/"));
        configuration.Authority.OriginalString.ShouldBe("https://login.example.com/");
        configuration.ClientId.ShouldBe("team-admin");
        configuration.Scopes.ShouldBe(["openid", "management"]);
        configuration.Branding.Name.ShouldBe("Acme");
        configuration.Branding.LogoUrl.ShouldBe(new Uri("img/logo.svg", UriKind.Relative));
        configuration.Branding.FaviconUrl.ShouldBe(new Uri("https://cdn.example.com/favicon.ico?v=2"));
        configuration.Branding.AccentColor.ShouldBe("#0F6CBD");
        configuration.Cultures.Offered.ShouldBe(["en", "de-CH"]);
        configuration.Cultures.Default.ShouldBe("de-CH");
        configuration.PreviewOrigins.ShouldBe(["https://www.example.com", "http://localhost:3000"]);
        configuration.PreviewEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://cms.example.com", "https://cms.example.com/")]
    [InlineData("https://cms.example.com/", "https://cms.example.com/")]
    [InlineData("https://example.com/cms", "https://example.com/cms/")]
    [InlineData("https://example.com/cms/", "https://example.com/cms/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void TheApiBaseUrlEndsWithASlashSoThatThePathBaseIsKept(string value, string expected)
    {
        var configuration = Valid($$"""{ "apiBaseUrl": "{{value}}" }""");

        configuration.ApiBaseUrl.AbsoluteUri.ShouldBe(expected);
        new Uri(configuration.ApiBaseUrl, "api/management/v1/").AbsoluteUri.ShouldBe(expected + "api/management/v1/");
    }

    [Fact]
    public void AMissingApiBaseUrlIsNamed()
    {
        var problem = Problems("""{ "clientId": "fluentcms-admin" }""").ShouldHaveSingleItem();

        problem.Kind.ShouldBe(ConfigurationProblemKind.Missing);
        problem.Member.ShouldBe("apiBaseUrl");
    }

    [Theory]
    [InlineData("\"cms.example.com\"")]
    [InlineData("\"/cms\"")]
    [InlineData("\"ftp://cms.example.com\"")]
    [InlineData("\"javascript:alert(1)\"")]
    [InlineData("\"file:///etc/passwd\"")]
    [InlineData("\"https://cms.example.com?x=1\"")]
    [InlineData("\"https://cms.example.com/#top\"")]
    [InlineData("\"https://user:secret@cms.example.com\"")]
    [InlineData("\" https://cms.example.com\"")]
    [InlineData("\"https:cms.example.com\"")]
    [InlineData("\"https://cms.example.com/a b\"")]
    [InlineData("\"https://cms.example.com\\\\cms\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("[\"https://cms.example.com\"]")]
    public void AnInvalidApiBaseUrlIsNamed(string value)
    {
        var problem = Problems($$"""{ "apiBaseUrl": {{value}} }""").ShouldHaveSingleItem();

        problem.Kind.ShouldBe(ConfigurationProblemKind.Invalid);
        problem.Member.ShouldBe("apiBaseUrl");
        problem.Rule.ShouldBe(ConfigurationRule.HttpUrl);
    }

    [Fact]
    public void AnInvalidAuthorityIsNamed()
    {
        var problem = Problems("""{ "apiBaseUrl": "https://cms.example.com", "authority": "login" }""").ShouldHaveSingleItem();

        problem.Member.ShouldBe("authority");
    }

    [Theory]
    [InlineData("\"#0f6cbd\"", true)]
    [InlineData("\"#abc\"", true)]
    [InlineData("\"#abcd\"", true)]
    [InlineData("\"#0f6cbd80\"", true)]
    [InlineData("\"red\"", false)]
    [InlineData("\"#0f6cbd; background: url(https://evil.example)\"", false)]
    [InlineData("\"#12345\"", false)]
    [InlineData("\"rgb(0, 0, 0)\"", false)]
    [InlineData("\"#0f6cbd\\n\"", false)]
    [InlineData("\"var(--x)\"", false)]
    [InlineData("12", false)]
    public void TheAccentColorIsAHexColor(string value, bool valid)
    {
        var startup = AdminConfigurationReader.Read($$"""{ "apiBaseUrl": "https://cms.example.com", "branding": { "accentColor": {{value}} } }""");

        if (valid)
        {
            startup.Configuration.ShouldNotBeNull();
        }
        else
        {
            var problem = startup.Problems.ShouldHaveSingleItem();
            problem.Member.ShouldBe("branding.accentColor");
            problem.Rule.ShouldBe(ConfigurationRule.HexColor);
        }
    }

    [Theory]
    [InlineData("logo.svg", true)]
    [InlineData("/assets/logo.svg", true)]
    [InlineData("https://cdn.example.com/logo.svg", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml,abc", false)]
    [InlineData("//evil.example/logo.svg", false)]
    [InlineData("logo.svg\\\")", false)]
    [InlineData("logo.svg') ; x", false)]
    [InlineData("logo .svg", false)]
    [InlineData("logo.svg#x", false)]
    [InlineData("https://user@cdn.example.com/logo.svg", false)]
    public void TheLogoIsAnHttpUrlOrARelativePath(string value, bool valid)
    {
        var startup = AdminConfigurationReader.Read($$"""{ "apiBaseUrl": "https://cms.example.com", "branding": { "logoUrl": "{{value}}" } }""");

        if (valid)
        {
            startup.Configuration.ShouldNotBeNull();
        }
        else
        {
            var problem = startup.Problems.ShouldHaveSingleItem();
            problem.Member.ShouldBe("branding.logoUrl");
            problem.Rule.ShouldBe(ConfigurationRule.UrlOrPath);
        }
    }

    [Theory]
    [InlineData("\"https://www.example.com/blog\"")]
    [InlineData("\"https://www.example.com?x\"")]
    [InlineData("\"www.example.com\"")]
    [InlineData("\"ftp://www.example.com\"")]
    [InlineData("null")]
    public void APreviewOriginIsAnOriginWithoutAPath(string value)
    {
        var problem = Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["https://ok.example.com", {{value}}] }""")
            .ShouldHaveSingleItem();

        problem.Member.ShouldBe("previewOrigins[1]");
        problem.Rule.ShouldBe(ConfigurationRule.Origin);
    }

    [Fact]
    public void PreviewOriginsAreNormalizedAndDistinct()
    {
        var configuration = Valid("""
            { "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["https://WWW.Example.com:443/", "https://www.example.com", "http://localhost:3000"] }
            """);

        configuration.PreviewOrigins.ShouldBe(["https://www.example.com", "http://localhost:3000"]);
    }

    [Fact]
    public void AnEmptyListOfPreviewOriginsTurnsPreviewOff()
    {
        Valid("""{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": [] }""").PreviewEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{ "offered": ["de", "fr"] }""", "cultures.default", nameof(ConfigurationRule.DefaultCultureOffered))]
    [InlineData("""{ "offered": ["en"], "default": "de" }""", "cultures.default", nameof(ConfigurationRule.DefaultCultureOffered))]
    [InlineData("""{ "offered": [] }""", "cultures.offered", nameof(ConfigurationRule.CultureList))]
    [InlineData("""{ "offered": ["en", "EN"] }""", "cultures.offered", nameof(ConfigurationRule.CultureList))]
    [InlineData("""{ "offered": ["en", "x"] }""", "cultures.offered[1]", nameof(ConfigurationRule.CultureName))]
    [InlineData("""{ "offered": "en" }""", "cultures.offered", nameof(ConfigurationRule.CultureList))]
    [InlineData("""{ "default": "en_US" }""", "cultures.default", nameof(ConfigurationRule.CultureName))]
    public void InvalidCulturesAreNamed(string cultures, string member, string rule)
    {
        var problem = Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "cultures": {{cultures}} }""").ShouldHaveSingleItem();

        problem.Member.ShouldBe(member);
        problem.Rule.ToString().ShouldBe(rule);
    }

    [Fact]
    public void TheDefaultCultureMayBeTheOnlyOneGiven()
    {
        var configuration = Valid("""{ "apiBaseUrl": "https://cms.example.com", "cultures": { "default": "qps-ploc" } }""");

        configuration.Cultures.Offered.ShouldBeNull();
        configuration.Cultures.Default.ShouldBe("qps-ploc");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"openid profile\"")]
    [InlineData("""["openid", "open id"]""")]
    [InlineData("""["openid", "openid"]""")]
    [InlineData("""["openid", 1]""")]
    [InlineData("""["openid", "a\"b"]""")]
    public void InvalidScopesAreNamed(string scopes)
    {
        var problem = Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "scopes": {{scopes}} }""").ShouldHaveSingleItem();

        problem.Member.ShouldBe("scopes");
        problem.Rule.ShouldBe(ConfigurationRule.ScopeList);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"my admin\"")]
    [InlineData("7")]
    public void AnInvalidClientIdIsNamed(string clientId)
    {
        Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "clientId": {{clientId}} }""").ShouldHaveSingleItem().Member.ShouldBe("clientId");
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    [InlineData("\" Acme\"")]
    [InlineData("\"Ac\\u0007me\"")]
    public void AnInvalidBrandNameIsNamed(string name)
    {
        Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "branding": { "name": {{name}} } }""").ShouldHaveSingleItem().Member.ShouldBe("branding.name");
    }

    [Theory]
    [InlineData("branding")]
    [InlineData("cultures")]
    public void ANestedMemberThatIsNotAnObjectIsNamed(string member)
    {
        var problem = Problems($$"""{ "apiBaseUrl": "https://cms.example.com", "{{member}}": "x" }""").ShouldHaveSingleItem();

        problem.Member.ShouldBe(member);
        problem.Rule.ShouldBe(ConfigurationRule.Object);
    }

    [Fact]
    public void EveryProblemIsNamedAtOnce()
    {
        var problems = Problems("""{ "authority": "x", "branding": { "accentColor": "red" }, "previewOrigins": ["x"] }""");

        problems.Select(problem => problem.Member).ShouldBe(["authority", "branding.accentColor", "previewOrigins[0]", "apiBaseUrl"], ignoreOrder: true);
    }

    [Fact]
    public void UnknownMembersAreIgnoredAndListed()
    {
        var startup = AdminConfigurationReader.Read("""
            { "apiBaseUrl": "https://cms.example.com", "theme": "dark", "branding": { "slogan": "x" }, "cultures": { "rtl": true } }
            """);

        startup.Configuration.ShouldNotBeNull();
        startup.UnknownMembers.ShouldBe(["theme", "branding.slogan", "cultures.rtl"]);
    }

    [Fact]
    public void UnknownMembersAreListedWhenTheFileIsInvalid()
    {
        var startup = AdminConfigurationReader.Read("""{ "theme": "dark" }""");

        startup.Configuration.ShouldBeNull();
        startup.UnknownMembers.ShouldBe(["theme"]);
    }

    [Theory]
    [InlineData("", nameof(ConfigurationProblemKind.NotJson))]
    [InlineData("{ apiBaseUrl: 'x' }", nameof(ConfigurationProblemKind.NotJson))]
    [InlineData("<html></html>", nameof(ConfigurationProblemKind.NotJson))]
    [InlineData("[]", nameof(ConfigurationProblemKind.NotAnObject))]
    [InlineData("\"https://cms.example.com\"", nameof(ConfigurationProblemKind.NotAnObject))]
    public void AFileThatIsNotAJsonObjectIsAProblemOfTheFile(string json, string kind)
    {
        var problem = Problems(json).ShouldHaveSingleItem();

        problem.Kind.ToString().ShouldBe(kind);
        problem.Member.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "apiBaseUrl": "https://cms.example.com", "clientId": "\ud800" }""")]
    [InlineData("""{ "apiBaseUrl": "https://a.example", "x\ud800": 1 }""")]
    [InlineData("""{ "apiBaseUrl": "https://a.example/\udc00" }""")]
    [InlineData("""{ "apiBaseUrl": "https://a.example", "branding": { "name": "\ud800" } }""")]
    public void AnUnpairedSurrogateIsNotJson(string json)
    {
        var problem = Problems(json).ShouldHaveSingleItem();

        problem.Kind.ToString().ShouldBe(nameof(ConfigurationProblemKind.NotJson));
    }

    [Theory]
    [InlineData("https://login.example.com/realms/acme")]
    [InlineData("https://login.example.com/realms/acme/")]
    [InlineData("https://login.example.com")]
    public void TheAuthorityIsKeptAsWritten(string authority)
    {
        var configuration = Valid($$"""{ "apiBaseUrl": "https://cms.example.com", "authority": "{{authority}}" }""");

        configuration.Authority.OriginalString.ShouldBe(authority);
    }

    [Fact]
    public void TheDefaultAuthorityIsTheApiBaseUrlAsWritten()
    {
        var configuration = Valid("""{ "apiBaseUrl": "https://example.com/cms" }""");

        configuration.ApiBaseUrl.AbsoluteUri.ShouldBe("https://example.com/cms/");
        configuration.Authority.OriginalString.ShouldBe("https://example.com/cms");
    }

    [Fact]
    public void TheListsCannotBeChangedThroughACast()
    {
        var configuration = Valid("""
            { "apiBaseUrl": "https://cms.example.com", "scopes": ["openid"], "cultures": { "offered": ["en"] }, "previewOrigins": ["https://www.example.com"] }
            """);

        foreach (var list in new[] { configuration.Scopes, configuration.Cultures.Offered!, configuration.PreviewOrigins })
        {
            list.ShouldNotBeAssignableTo<List<string>>();
            ((ICollection<string>)list).IsReadOnly.ShouldBeTrue();
            Should.Throw<NotSupportedException>(() => ((ICollection<string>)list).Add("x"));
        }
    }

    [Theory]
    [InlineData("""{ "apiBaseUrl": "https://good.example", "authority": "https://good.example", "authority": "https://evil.example" }""", "authority")]
    [InlineData("""{ "apiBaseUrl": "https://a.example", "apiBaseUrl": "https://b.example" }""", "apiBaseUrl")]
    [InlineData("""{ "apiBaseUrl": "https://a.example", "branding": { "name": "A", "name": "B" } }""", "branding.name")]
    [InlineData("""{ "apiBaseUrl": "https://a.example", "cultures": { "default": "en", "default": "de" } }""", "cultures.default")]
    public void ADuplicateMemberIsRefused(string json, string member)
    {
        var problem = Problems(json).ShouldHaveSingleItem();

        problem.Kind.ToString().ShouldBe(nameof(ConfigurationProblemKind.Duplicate));
        problem.Member.ShouldBe(member);
    }

    [Fact]
    public void TheDefaultCultureTakesTheSpellingOfTheOfferedOne()
    {
        var configuration = Valid("""{ "apiBaseUrl": "https://cms.example.com", "cultures": { "offered": ["EN", "de-ch"], "default": "en" } }""");

        configuration.Cultures.Default.ShouldBe("EN");
        configuration.Cultures.Offered!.ShouldContain(configuration.Cultures.Default);
    }

    [Theory]
    [InlineData("""{ "apiBaseUrl": "http://cms.example.com" }""", new[] { "apiBaseUrl", "authority" })]
    [InlineData("""{ "apiBaseUrl": "http://cms.example.com", "authority": "https://login.example.com" }""", new[] { "apiBaseUrl" })]
    [InlineData("""{ "apiBaseUrl": "http://localhost:8080", "authority": "http://127.0.0.1:8080" }""", new string[0])]
    [InlineData("""{ "apiBaseUrl": "https://cms.example.com" }""", new string[0])]
    public void PlainHttpOnARemoteHostIsNoted(string json, string[] members)
    {
        AdminConfigurationReader.Read(json).PlainHttpMembers.ShouldBe(members);
    }

    [Fact]
    public void ANonAsciiHostIsRefusedWithAHintAtItsPunycodeForm()
    {
        var problem = Problems("""{ "apiBaseUrl": "https://bücher.example" }""").ShouldHaveSingleItem();

        ConfigurationTexts.Describe(NeutralTexts(), problem).ShouldContain("ASCII only; write an internationalized host name in its xn-- form");
        Valid("""{ "apiBaseUrl": "https://xn--bcher-kva.example" }""").ApiBaseUrl.Host.ShouldBe("xn--bcher-kva.example");
    }

    private static StringLocalizer<AdminResources> NeutralTexts() =>
        new StringLocalizer<AdminResources>(
            new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static AdminConfiguration Valid(string json)
    {
        var startup = AdminConfigurationReader.Read(json);
        startup.Problems.ShouldBeEmpty();
        return startup.Configuration.ShouldNotBeNull();
    }

    private static IReadOnlyList<ConfigurationProblem> Problems(string json)
    {
        var startup = AdminConfigurationReader.Read(json);
        startup.Configuration.ShouldBeNull();
        return startup.Problems;
    }
}
