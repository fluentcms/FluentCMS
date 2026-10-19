namespace FluentCMS.IntegrationTests.Browser;

// Runs only when the variable is set, because the tests need a browser that Playwright installed; CI installs Chromium and sets it.
public sealed class BrowserFactAttribute : FactAttribute
{
    public const string VariableName = "FLUENTCMS_TEST_BROWSER";

    public BrowserFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName)))
        {
            Skip = $"{VariableName} is not set.";
        }
    }
}
