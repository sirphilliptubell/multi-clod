using Velopack;

namespace MultiClod.App.Updates;

/// <summary>
/// The one production implementation of <see cref="IUpdateManager"/> - a straight pass-through to
/// the real <see cref="Velopack.UpdateManager"/>. This is the only file that touches the concrete
/// Velopack type directly; everything else goes through the interface so it can be faked in tests.
/// </summary>
internal sealed class VelopackUpdateManagerAdapter : IUpdateManager {
	private readonly UpdateManager inner;

	public VelopackUpdateManagerAdapter(string feedPath) {
		this.inner = new UpdateManager(feedPath);
	}

	public Task<UpdateInfo?> CheckForUpdatesAsync() => this.inner.CheckForUpdatesAsync();

	public Task DownloadUpdatesAsync(UpdateInfo updateInfo) => this.inner.DownloadUpdatesAsync(updateInfo);

	public void ApplyUpdatesAndRestart(VelopackAsset? toApply = null, string[]? restartArgs = null) =>
		this.inner.ApplyUpdatesAndRestart(toApply, restartArgs);

	public VelopackAsset? UpdatePendingRestart => this.inner.UpdatePendingRestart;

	public SemanticVersion? CurrentVersion => this.inner.CurrentVersion;

	public void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null) =>
		this.inner.WaitExitThenApplyUpdates(toApply, silent, restart, restartArgs);
}
