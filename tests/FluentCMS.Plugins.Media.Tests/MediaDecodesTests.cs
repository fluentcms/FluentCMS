using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaDecodesTests
{
    [Fact]
    public async Task CallsForOneKeyWhileItRunsShareOneRun()
    {
        using var decodes = Decodes(4);
        var gate = Gate();
        var runs = 0;

        async Task Work(CancellationToken token)
        {
            Interlocked.Increment(ref runs);
            await gate.Task;
        }

        Task[] calls = [decodes.RunAsync("variant", Work, Token), decodes.RunAsync("variant", Work, Token), decodes.RunAsync("variant", Work, Token)];
        decodes.Joined.ShouldBe(2);
        gate.SetResult();
        await Task.WhenAll(calls);

        runs.ShouldBe(1);
    }

    [Fact]
    public async Task NoMoreRunsThanTheLimitRunAtOnce()
    {
        using var decodes = Decodes(2);
        var running = 0;
        var most = 0;
        var count = 0;
        var two = Gate();
        var gates = new[] { Gate(), Gate(), Gate() };
        var entered = new[] { Gate(), Gate(), Gate() };

        Func<CancellationToken, Task> Work(int index) => async _ =>
        {
            InterlockedMax(ref most, Interlocked.Increment(ref running));
            entered[index].SetResult();
            if (Interlocked.Increment(ref count) == 2)
            {
                two.SetResult();
            }

            await gates[index].Task;
            Interlocked.Decrement(ref running);
        };

        Task[] calls = [.. Enumerable.Range(0, 3).Select(index => decodes.RunAsync($"variant-{index}", Work(index), Token))];
        await two.Task.WaitAsync(Token);
        var waiting = entered.Single(gate => !gate.Task.IsCompleted);

        gates[Array.FindIndex(entered, gate => gate.Task.IsCompleted)].SetResult();
        await waiting.Task.WaitAsync(Token);
        foreach (var gate in gates)
        {
            gate.TrySetResult();
        }

        await Task.WhenAll(calls);
        most.ShouldBe(2);
    }

    [Fact]
    public async Task ACallerThatGivesUpStopsWaitingAndTheRunGoesOnForTheOthers()
    {
        using var decodes = Decodes(4);
        var gate = Gate();
        var entered = Gate();
        var cancelled = false;

        async Task Work(CancellationToken token)
        {
            entered.SetResult();
            await gate.Task;
            cancelled = token.IsCancellationRequested;
        }

        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var leader = decodes.RunAsync("variant", Work, leaving.Token);
        var staying = decodes.RunAsync("variant", Work, Token);
        await entered.Task.WaitAsync(Token);

        await leaving.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(leader);
        staying.IsCompleted.ShouldBeFalse();
        gate.SetResult();
        await staying;

        cancelled.ShouldBeFalse();
        decodes.FreeSlots.ShouldBe(4);
    }

    [Fact]
    public async Task ARunStillQueuedWhoseOnlyCallerLeavesNeverDecodesAndTakesNoSlot()
    {
        using var decodes = Decodes(1);
        var gate = Gate();
        var entered = Gate();
        var queuedRuns = 0;

        var holding = decodes.RunAsync("holding", async _ => { entered.SetResult(); await gate.Task; }, Token);
        await entered.Task.WaitAsync(Token);
        decodes.FreeSlots.ShouldBe(0);

        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var queued = decodes.RunAsync("queued", _ => { Interlocked.Increment(ref queuedRuns); return Task.CompletedTask; }, leaving.Token);
        await leaving.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(queued);

        // A call after the leave starts a run of its own rather than joining the cancelled one.
        var again = decodes.RunAsync("queued", _ => { Interlocked.Increment(ref queuedRuns); return Task.CompletedTask; }, Token);
        gate.SetResult();
        await holding;
        await again;

        // The slot is FIFO, so the cancelled run, had it stayed queued, would have run before the new one.
        queuedRuns.ShouldBe(1);
        decodes.FreeSlots.ShouldBe(1);
        decodes.Joined.ShouldBe(0);
    }

    [Fact]
    public async Task ARunStillQueuedGoesOnWhileACallerWaits()
    {
        using var decodes = Decodes(1);
        var gate = Gate();
        var entered = Gate();
        var queuedRuns = 0;

        var holding = decodes.RunAsync("holding", async _ => { entered.SetResult(); await gate.Task; }, Token);
        await entered.Task.WaitAsync(Token);

        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var left = decodes.RunAsync("queued", _ => { Interlocked.Increment(ref queuedRuns); return Task.CompletedTask; }, leaving.Token);
        var staying = decodes.RunAsync("queued", _ => { Interlocked.Increment(ref queuedRuns); return Task.CompletedTask; }, Token);
        await leaving.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(left);
        gate.SetResult();
        await holding;
        await staying;

        queuedRuns.ShouldBe(1);
        decodes.FreeSlots.ShouldBe(1);
    }

    [Fact]
    public async Task ARunThatFailsFailsEveryCallerAndTheNextCallRunsAgain()
    {
        using var decodes = Decodes(4);
        var gate = Gate();
        var runs = 0;

        async Task Failing(CancellationToken token)
        {
            Interlocked.Increment(ref runs);
            await gate.Task;
            throw new InvalidOperationException("No image.");
        }

        var first = decodes.RunAsync("variant", Failing, Token);
        var second = decodes.RunAsync("variant", Failing, Token);
        gate.SetResult();

        (await Should.ThrowAsync<InvalidOperationException>(first)).Message.ShouldBe("No image.");
        (await Should.ThrowAsync<InvalidOperationException>(second)).Message.ShouldBe("No image.");
        await decodes.RunAsync("variant", _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, Token);
        runs.ShouldBe(2);
    }

    [Fact]
    public async Task AnOriginalNotOnDiskIsCopiedToATemporaryFileThatGoesWithItsStream()
    {
        List<string> files = [];
        var bytes = Transforms.Bands(40, 20);

        await using (var spooled = await MediaTransforms.SpoolAsync(new SkiaImageProcessorTests.ForwardOnlyStream(bytes), Recorded(files), Token))
        {
            spooled.ShouldBeOfType<FileStream>().Name.ShouldBe(files.Single());
            using var copy = new MemoryStream();
            await spooled.CopyToAsync(copy, Token);
            copy.ToArray().ShouldBe(bytes);
            File.Exists(files[0]).ShouldBeTrue();
        }

        File.Exists(files[0]).ShouldBeFalse();
        await using var inMemory = new MemoryStream(bytes);
        (await MediaTransforms.SpoolAsync(inMemory, Recorded(files), Token)).ShouldBeSameAs(inMemory);
        files.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ACopyThatFailsOrIsCancelledLeavesNoTemporaryFile()
    {
        List<string> files = [];
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<IOException>(MediaTransforms.SpoolAsync(new FailingStream(), Recorded(files), Token));
        await Should.ThrowAsync<OperationCanceledException>(MediaTransforms.SpoolAsync(new SkiaImageProcessorTests.ForwardOnlyStream(Transforms.Bands(40, 20)), Recorded(files), cancelled.Token));

        files.Count.ShouldBe(2);
        files.ShouldAllBe(file => !File.Exists(file));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void TheLimitIsFromOneTo256(int limit, bool valid) =>
        new MediaOptions { MaxConcurrentDecodes = limit }.IsValid().ShouldBe(valid);

    private static MediaDecodes Decodes(int limit) =>
        new(Options.Create(new MediaOptions { MaxConcurrentDecodes = limit }), new Lifetime());

    private static Func<FileStream> Recorded(List<string> files) => () =>
    {
        var file = MediaService.TemporaryFile();
        files.Add(file.Name);
        return file;
    };

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    // Gives some bytes, then fails, as a storage whose connection drops.
    private sealed class FailingStream : Stream
    {
        private bool _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read)
            {
                throw new IOException("The connection dropped.");
            }

            _read = true;
            buffer[offset] = 1;
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
