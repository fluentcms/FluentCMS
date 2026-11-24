using FluentCMS.Admin.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

// The copy of the interface culture in localStorage (spec 3.1, 4.17): one key, a culture name and nothing else, read before
// the host runs through the runtime the builder registered.
public sealed class CultureStorageTests
{
    [Fact]
    public async Task TheCopyHoldsACultureNameAndNothingElse()
    {
        var js = new FakeLocalStorage();
        var storage = new CultureStorageInterop(js, NullLogger<CultureStorageInterop>.Instance);

        (await storage.WriteAsync("fr-ca")).ShouldBeTrue();
        js.Items.ShouldBe(new Dictionary<string, string> { [CultureStorageInterop.Key] = "fr-CA" });
        (await storage.ReadAsync()).ShouldBe("fr-CA");

        (await storage.WriteAsync("<script>")).ShouldBeTrue();
        js.Items.ShouldBeEmpty();

        js.Items[CultureStorageInterop.Key] = new string('a', 40);
        (await storage.ReadAsync()).ShouldBeNull();
        (await storage.WriteAsync(null)).ShouldBeTrue();
        js.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task StorageTheBrowserRefusesReadsAsNoCopyAndKeepsNothing()
    {
        var storage = new CultureStorageInterop(new FakeLocalStorage { Refuses = true }, NullLogger<CultureStorageInterop>.Instance);

        (await storage.ReadAsync()).ShouldBeNull();
        (await storage.WriteAsync("fr")).ShouldBeFalse();
    }

    [Fact]
    public async Task TheStartReadsTheCopyThroughTheRuntimeTheBuilderRegistered()
    {
        var js = new FakeLocalStorage();
        js.Items[CultureStorageInterop.Key] = "ar";
        var services = new ServiceCollection();

        (await CultureStorageInterop.ReadAtStartAsync(services)).ShouldBeNull();

        services.AddSingleton<IJSRuntime>(js);
        (await CultureStorageInterop.ReadAtStartAsync(services)).ShouldBe("ar");
    }

    private sealed class FakeLocalStorage : IJSRuntime
    {
        public Dictionary<string, string> Items { get; } = [];

        public bool Refuses { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (Refuses)
            {
                throw new JSException("The storage is refused.");
            }

            var key = (string)args![0]!;
            object? result = null;
            switch (identifier)
            {
                case "localStorage.getItem":
                    result = Items.GetValueOrDefault(key);
                    break;
                case "localStorage.setItem":
                    Items[key] = (string)args[1]!;
                    break;
                case "localStorage.removeItem":
                    Items.Remove(key);
                    break;
                default:
                    throw new InvalidOperationException(identifier);
            }

            return ValueTask.FromResult((TValue)result!);
        }
    }
}
