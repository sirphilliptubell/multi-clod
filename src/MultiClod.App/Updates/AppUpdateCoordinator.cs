using System.Windows.Threading;
using Velopack;

namespace MultiClod.App.Updates;

/// <summary>
/// Owns all update-related decision logic for the app, against an <see cref="IUpdateManager"/>
/// seam so it's testable without a real Velopack install. No-ops entirely when constructed with a
/// null manager (no feed configured - e.g. a plain local debug build).
/// </summary>
public sealed class AppUpdateCoordinator {
	private readonly IUpdateManager? manager;

	public AppUpdateCoordinator(IUpdateManager? manager) {
		this.manager = manager;
	}

	public static AppUpdateCoordinator CreateForRuntime() {
		var feedPath = UpdateFeedLocation.Path;
		return new AppUpdateCoordinator(feedPath is null ? null : new VelopackUpdateManagerAdapter(feedPath));
	}

	/// <summary>
	/// True if this call is about to exit the process (an update was found, downloaded, and
	/// ApplyUpdatesAndRestart was called) - the caller must not proceed past a true result, since
	/// the buggy build's own UI should never get a chance to show.
	/// </summary>
	public bool RunStartupCheckAndApplyIfFound(string[] originalArgs) {
		if (this.manager is null) {
			return false;
		}

		var newVersion = this.manager.CheckForUpdatesAsync().GetAwaiter().GetResult();
		if (newVersion is null) {
			return false;
		}

		this.manager.DownloadUpdatesAsync(newVersion).GetAwaiter().GetResult();
		this.manager.ApplyUpdatesAndRestart(newVersion, restartArgs: originalArgs); // never returns
		return true;
	}

	public void StartPeriodicChecks(Dispatcher dispatcher) {
		if (this.manager is null) {
			return;
		}

		var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMinutes(5) };
		timer.Tick += (_, _) => { _ = Task.Run(this.CheckAndDownloadInBackgroundAsync); };
		timer.Start(); // first tick at +5min - the startup path above already did an immediate check
	}

	private async Task CheckAndDownloadInBackgroundAsync() {
		try {
			if (this.manager!.UpdatePendingRestart is not null) {
				return; // already downloaded, nothing to do until it's applied (on exit or on crash)
			}

			var newVersion = await this.manager.CheckForUpdatesAsync();
			if (newVersion is null) {
				return;
			}

			await this.manager.DownloadUpdatesAsync(newVersion);
			// Deliberately not applying/restarting here - see ApplyPendingUpdateOnExit and
			// TryApplyPendingUpdateOnCrash, the only two places a downloaded update gets applied.
		}
		catch {
			// Best-effort; retried on the next 5-minute tick.
		}
	}

	public void ApplyPendingUpdateOnExit() {
		if (this.manager?.UpdatePendingRestart is { } pending) {
			this.manager.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
		}
	}

	/// <summary>
	/// Called from both crash handlers. Returns true if it triggered ApplyUpdatesAndRestart
	/// (the process is exiting) - the caller should let the original crash proceed if false.
	/// </summary>
	public bool TryApplyPendingUpdateOnCrash() {
		if (this.manager?.UpdatePendingRestart is { } pending) {
			this.manager.ApplyUpdatesAndRestart(pending);
			return true;
		}

		return false;
	}
}
