using MultiClod.App.Updates;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Velopack;

namespace MultiClod.App.Tests;

public sealed class AppUpdateCoordinatorTests
{
    [Test]
    public async Task RunStartupCheckAndApplyIfFound_NoManager_ReturnsFalseAndDoesNothing()
    {
        var coordinator = new AppUpdateCoordinator(manager: null);

        var result = coordinator.RunStartupCheckAndApplyIfFound([]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RunStartupCheckAndApplyIfFound_NoUpdateAvailable_ReturnsFalse()
    {
        var fake = new FakeUpdateManager { CheckForUpdatesResult = null };
        var coordinator = new AppUpdateCoordinator(fake);

        var result = coordinator.RunStartupCheckAndApplyIfFound([]);

        await Assert.That(result).IsFalse();
        await Assert.That(fake.ApplyUpdatesAndRestartCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task RunStartupCheckAndApplyIfFound_UpdateAvailable_DownloadsAndAppliesAndReturnsTrue()
    {
        var newVersion = FakeUpdateManager.CreateUpdateInfo("1.1.0");
        var fake = new FakeUpdateManager { CheckForUpdatesResult = newVersion };
        var coordinator = new AppUpdateCoordinator(fake);
        var args = new[] { "--from-here", @"C:\some\dir" };

        var result = coordinator.RunStartupCheckAndApplyIfFound(args);

        await Assert.That(result).IsTrue();
        await Assert.That(fake.DownloadUpdatesCallCount).IsEqualTo(1);
        await Assert.That(fake.ApplyUpdatesAndRestartCallCount).IsEqualTo(1);
        await Assert.That(fake.LastRestartArgs).IsEquivalentTo(args);
    }

    [Test]
    public async Task TryApplyPendingUpdateOnCrash_NoManager_ReturnsFalse()
    {
        var coordinator = new AppUpdateCoordinator(manager: null);

        var result = coordinator.TryApplyPendingUpdateOnCrash();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryApplyPendingUpdateOnCrash_NothingPending_ReturnsFalse()
    {
        var fake = new FakeUpdateManager { UpdatePendingRestart = null };
        var coordinator = new AppUpdateCoordinator(fake);

        var result = coordinator.TryApplyPendingUpdateOnCrash();

        await Assert.That(result).IsFalse();
        await Assert.That(fake.ApplyUpdatesAndRestartCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryApplyPendingUpdateOnCrash_UpdateAlreadyPending_AppliesAndReturnsTrue()
    {
        var pending = FakeUpdateManager.CreateAsset("1.2.0");
        var fake = new FakeUpdateManager { UpdatePendingRestart = pending };
        var coordinator = new AppUpdateCoordinator(fake);

        var result = coordinator.TryApplyPendingUpdateOnCrash();

        await Assert.That(result).IsTrue();
        await Assert.That(fake.ApplyUpdatesAndRestartCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ApplyPendingUpdateOnExit_NothingPending_DoesNotCallWaitExitThenApplyUpdates()
    {
        var fake = new FakeUpdateManager { UpdatePendingRestart = null };
        var coordinator = new AppUpdateCoordinator(fake);

        coordinator.ApplyPendingUpdateOnExit();

        await Assert.That(fake.WaitExitThenApplyUpdatesCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyPendingUpdateOnExit_UpdatePending_AppliesSilentlyWithoutRestart()
    {
        var pending = FakeUpdateManager.CreateAsset("1.2.0");
        var fake = new FakeUpdateManager { UpdatePendingRestart = pending };
        var coordinator = new AppUpdateCoordinator(fake);

        coordinator.ApplyPendingUpdateOnExit();

        await Assert.That(fake.WaitExitThenApplyUpdatesCallCount).IsEqualTo(1);
        await Assert.That(fake.LastWaitExitSilent).IsTrue();
        await Assert.That(fake.LastWaitExitRestart).IsFalse();
    }

    private sealed class FakeUpdateManager : IUpdateManager
    {
        public UpdateInfo? CheckForUpdatesResult { get; set; }

        public VelopackAsset? UpdatePendingRestart { get; set; }

        public SemanticVersion? CurrentVersion { get; set; }

        public int DownloadUpdatesCallCount { get; private set; }

        public int ApplyUpdatesAndRestartCallCount { get; private set; }

        public int WaitExitThenApplyUpdatesCallCount { get; private set; }

        public string[]? LastRestartArgs { get; private set; }

        public bool LastWaitExitSilent { get; private set; }

        public bool LastWaitExitRestart { get; private set; }

        public Task<UpdateInfo?> CheckForUpdatesAsync() => Task.FromResult(this.CheckForUpdatesResult);

        public Task DownloadUpdatesAsync(UpdateInfo updateInfo)
        {
            this.DownloadUpdatesCallCount++;
            return Task.CompletedTask;
        }

        public void ApplyUpdatesAndRestart(VelopackAsset? toApply = null, string[]? restartArgs = null)
        {
            this.ApplyUpdatesAndRestartCallCount++;
            this.LastRestartArgs = restartArgs;
        }

        public void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null)
        {
            this.WaitExitThenApplyUpdatesCallCount++;
            this.LastWaitExitSilent = silent;
            this.LastWaitExitRestart = restart;
        }

        public static VelopackAsset CreateAsset(string version) => new() {
            PackageId = "MultiClod.App",
            Version = SemanticVersion.Parse(version),
            Type = VelopackAssetType.Full,
            FileName = $"MultiClod.App-{version}-full.nupkg",
            SHA1 = string.Empty,
            SHA256 = string.Empty,
            Size = 0,
        };

        public static UpdateInfo CreateUpdateInfo(string version) =>
            new(CreateAsset(version), false, null, []);
    }
}
