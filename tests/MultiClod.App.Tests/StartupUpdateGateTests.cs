using MultiClod.App.Updates;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class StartupUpdateGateTests
{
    [Test]
    public async Task WaitForCheckToFinish_AlreadySignaled_ReturnsImmediately()
    {
        var gate = new StartupUpdateGate();

        var elapsed = await MeasureAsync(() => gate.WaitForCheckToFinish(timeoutMs: 5000));

        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task WaitForCheckToFinish_DuringBeginCheck_BlocksUntilEndCheck()
    {
        var gate = new StartupUpdateGate();
        gate.BeginCheck();

        var waitTask = Task.Run(() => gate.WaitForCheckToFinish(timeoutMs: 5000));
        await Task.Delay(200); // give the background wait a chance to actually start blocking

        await Assert.That(waitTask.IsCompleted).IsFalse();

        gate.EndCheck();
        await waitTask;

        await Assert.That(waitTask.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task WaitForCheckToFinish_TimeoutElapsesBeforeEndCheck_ReturnsAnyway()
    {
        var gate = new StartupUpdateGate();
        gate.BeginCheck();

        var elapsed = await MeasureAsync(() => gate.WaitForCheckToFinish(timeoutMs: 200));

        await Assert.That(elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    private static async Task<TimeSpan> MeasureAsync(Action action)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(action);
        return stopwatch.Elapsed;
    }
}
