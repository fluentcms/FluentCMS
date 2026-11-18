using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaDropZoneTests : MediaTestContext
{
    private readonly FakeRuntime runtime = new();

    public MediaDropZoneTests() => Services.AddSingleton<IJSRuntime>(runtime);

    [Fact]
    public async Task RendersWhileTheModuleLoadsListenOnceAndDisposingRemovesTheListeners()
    {
        var module = new FakeModule();

        var cut = Render<MediaDropZone>(parameters => parameters.Add(zone => zone.Enabled, true));
        cut.Render(parameters => parameters.Add(zone => zone.Class, "media-library"));
        cut.Render(parameters => parameters.Add(zone => zone.Class, "media-library media-wide"));
        runtime.Import.SetResult(module);

        cut.WaitForAssertion(() => module.Listeners.Count.ShouldBe(1));
        cut.Render(parameters => parameters.Add(zone => zone.Class, "media-library"));
        module.Listeners.Count.ShouldBe(1);
        runtime.Imports.ShouldBe(1);

        await DisposeComponentsAsync();
        module.Listeners.Single().Disposed.ShouldBeTrue();
        module.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task AZoneDisposedWhileTheModuleLoadsNeverListens()
    {
        var module = new FakeModule();
        Render<MediaDropZone>(parameters => parameters.Add(zone => zone.Enabled, true));

        await DisposeComponentsAsync();
        runtime.Import.SetResult(module);
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        module.Listeners.ShouldBeEmpty();
    }

    [Fact]
    public void AModuleThatDoesNotLoadLeavesTheZoneAsItIs()
    {
        var cut = Render<MediaDropZone>(parameters => parameters
            .Add(zone => zone.Enabled, true)
            .Add(zone => zone.ChildContent, "<p>Library</p>"));

        runtime.Import.SetException(new JSException("Failed to fetch the module."));

        cut.WaitForAssertion(() => cut.Find("p").TextContent.ShouldBe("Library"));
        cut.Render(parameters => parameters.Add(zone => zone.Class, "media-library"));
        runtime.Imports.ShouldBe(1);
    }

    [Fact]
    public void ADisabledZoneLoadsNothing()
    {
        Render<MediaDropZone>(parameters => parameters.Add(zone => zone.Enabled, false));

        runtime.Imports.ShouldBe(0);
    }

    // Answers the import of the module when the test says so.
    private sealed class FakeRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Imports { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("import");
            args.ShouldBe([MediaDropZone.ModulePath]);
            Imports++;
            return (TValue)await Import.Task;
        }
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public List<FakeListener> Listeners { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("listen");
            var listener = new FakeListener();
            Listeners.Add(listener);
            return ValueTask.FromResult((TValue)(object)listener);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeListener : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("dispose");
            Disposed = true;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
