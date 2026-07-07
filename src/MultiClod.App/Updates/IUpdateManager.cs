using Velopack;

namespace MultiClod.App.Updates;

/// <summary>
/// Thin seam over the handful of <see cref="Velopack.UpdateManager"/> members
/// <see cref="AppUpdateCoordinator"/> needs. The real UpdateManager throws NotInstalledException
/// unless running from an actual installed build, which would otherwise make the coordinator's
/// decision logic untestable.
/// </summary>
public interface IUpdateManager {
	Task<UpdateInfo?> CheckForUpdatesAsync();

	Task DownloadUpdatesAsync(UpdateInfo updateInfo);

	void ApplyUpdatesAndRestart(VelopackAsset? toApply = null, string[]? restartArgs = null);

	VelopackAsset? UpdatePendingRestart { get; }

	void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null);
}
