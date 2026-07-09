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

	// Marshaling target for StatusChanged, set once by StartPeriodicChecks. Null until then (and
	// permanently, when manager is null) - RunStartupCheckAndApplyIfFound's own SetStatus calls run
	// before it's set, but that's fine: nothing has subscribed to StatusChanged yet at that point
	// anyway (MainWindow doesn't exist), so the direct-invoke branch below is a no-op.
	private Dispatcher? dispatcher;

	// Null means "no update feed configured" (manager is null) - distinct from any real status, so
	// MainWindow can tell "nothing to report, ever" apart from "checked once and it's UpToDate".
	private AppUpdateStatus? status;

	/// <summary>
	/// Fired whenever <see cref="Status"/> changes, always marshaled onto the Dispatcher passed to
	/// <see cref="StartPeriodicChecks"/> (once one exists) so subscribers never need to hop threads
	/// themselves.
	/// </summary>
	public event Action<AppUpdateStatus>? StatusChanged;

	public AppUpdateStatus? Status => this.status;

	/// <summary>
	/// The currently-running version, e.g. "0.0.1" - null whenever <see cref="Status"/> would also
	/// be null (no manager), plus the rarer case of a manager that exists but isn't actually running
	/// from an installed location (NotInstalledException). A plain string rather than
	/// <see cref="Velopack.SemanticVersion"/> so callers (MainWindow) don't need a Velopack
	/// reference just to show it.
	/// </summary>
	public string? CurrentVersionText { get; }

	public AppUpdateCoordinator(IUpdateManager? manager) {
		this.manager = manager;
		this.CurrentVersionText = TryGetCurrentVersionText(manager);
	}

	private static string? TryGetCurrentVersionText(IUpdateManager? manager) {
		try {
			return manager?.CurrentVersion?.ToString();
		}
		catch {
			// e.g. NotInstalledException - a manager exists (a feed is configured) but this process
			// isn't actually running from a real Velopack install.
			return null;
		}
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

		this.SetStatus(AppUpdateStatus.CheckingForUpdates);
		var newVersion = this.manager.CheckForUpdatesAsync().GetAwaiter().GetResult();
		if (newVersion is null) {
			this.SetStatus(AppUpdateStatus.UpToDate);
			return false;
		}

		this.SetStatus(AppUpdateStatus.DownloadingUpdate);
		this.manager.DownloadUpdatesAsync(newVersion).GetAwaiter().GetResult();
		this.manager.ApplyUpdatesAndRestart(newVersion, restartArgs: originalArgs); // never returns
		return true;
	}

	public void StartPeriodicChecks(Dispatcher dispatcher) {
		if (this.manager is null) {
			return;
		}

		this.dispatcher = dispatcher;

		var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMinutes(5) };
		timer.Tick += (_, _) => { _ = Task.Run(this.CheckAndDownloadInBackgroundAsync); };
		timer.Start(); // first tick at +5min - the startup path above already did an immediate check
	}

	private async Task CheckAndDownloadInBackgroundAsync() {
		if (this.manager!.UpdatePendingRestart is not null) {
			return; // already downloaded, nothing to do until it's applied (on exit or on crash)
		}

		// Restored on a failed attempt below, rather than leaving the title stuck on "Checking..."
		// /"Downloading..." for the 5 minutes until the next tick retries.
		var previousStatus = this.status;
		try {
			this.SetStatus(AppUpdateStatus.CheckingForUpdates);
			var newVersion = await this.manager.CheckForUpdatesAsync();
			if (newVersion is null) {
				this.SetStatus(AppUpdateStatus.UpToDate);
				return;
			}

			this.SetStatus(AppUpdateStatus.DownloadingUpdate);
			await this.manager.DownloadUpdatesAsync(newVersion);
			// Deliberately not applying/restarting here - see ApplyPendingUpdateOnExit and
			// TryApplyPendingUpdateOnCrash, the only two places a downloaded update gets applied.
			this.SetStatus(AppUpdateStatus.RestartToUpdate);
		}
		catch {
			// Best-effort; retried on the next 5-minute tick.
			if (previousStatus is { } restore) {
				this.SetStatus(restore);
			}
		}
	}

	/// <summary>
	/// Updates <see cref="Status"/> and raises <see cref="StatusChanged"/>, marshaled onto
	/// <see cref="dispatcher"/> when the call didn't originate on that thread (e.g. from inside
	/// <see cref="CheckAndDownloadInBackgroundAsync"/>, which runs via Task.Run off the UI thread).
	/// </summary>
	private void SetStatus(AppUpdateStatus newStatus) {
		this.status = newStatus;

		if (this.dispatcher is null || this.dispatcher.CheckAccess()) {
			this.StatusChanged?.Invoke(newStatus);
		}
		else {
			this.dispatcher.BeginInvoke(() => this.StatusChanged?.Invoke(newStatus));
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
