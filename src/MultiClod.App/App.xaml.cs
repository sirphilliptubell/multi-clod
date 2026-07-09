using MultiClod.App.FromHere;
using MultiClod.App.Splash;
using MultiClod.App.Updates;
using MultiClod.Shared;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace MultiClod.App;

/// <summary>
/// Interaction logic for App.xaml. Owns global single-instance enforcement: exactly one
/// MultiClod.App process may be running at a time (see the "Multi-Clod from here" plan). Every
/// launch - whether a plain double-click or one driven by MultiClod.FromHere's --from-here
/// argument - tries to acquire <see cref="FromHereProtocol.MutexName"/> first; losing it means
/// handing off to whichever process already holds it (over <see cref="FromHereProtocol.PipeName"/>)
/// and never creating a window of our own.
/// </summary>
public partial class App : Application {
	// Signaled ("safe to crash") except while the synchronous startup update check below is
	// actually in flight - see HandleCrash.
	private static readonly StartupUpdateGate startupUpdateGate = new();

	// Bound on how long a crash waits for an in-flight startup check to conclude. Everywhere
	// else this is a no-op wait (already signaled), so this only adds latency to the narrow set
	// of crashes racing the startup check itself.
	private const int CrashGraceMs = 3000;

	private Mutex? singleInstanceMutex;
	private CancellationTokenSource? pipeServerCancellation;
	private AppUpdateCoordinator? updateCoordinator;

	[STAThread]
	public static void Main(string[] args) {
		// Registered before VelopackApp.Build().Run() so it also covers exceptions thrown while
		// Velopack itself is doing its (rare) install/uninstall hook work.
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

		// Must be the first real Velopack call: handles Velopack's own lifecycle hooks (e.g.
		// shortcut creation on install) and exits immediately for those cases, before any of our
		// app logic runs. Also auto-applies any update left pending from a prior session where
		// OnExit never got to run (e.g. a hard crash) - complementary to, not a replacement for,
		// the crash-time self-heal in HandleCrash below.
		VelopackApp.Build().Run();

		var app = new App();
		app.InitializeComponent();
		app.Run();
	}

	/// <summary>
	/// Both from-here delivery paths - this process's own --from-here startup argument, and the
	/// pipe server below (a hand-off from a later MultiClod.FromHere invocation) - post through
	/// here rather than raising an event directly, so a request posted before MainWindow exists to
	/// attach a handler (StartupUri's window isn't constructed until after OnStartup returns) is
	/// buffered instead of silently lost. MainWindow attaches once it exists - see its Loaded
	/// handler.
	/// </summary>
	public FromHereRequestQueue FromHereRequests { get; } = new();

	/// <summary>
	/// Exposed so MainWindow can reflect <see cref="AppUpdateCoordinator.StatusChanged"/> in the
	/// title bar - null only when constructed with a null manager, e.g. no feed configured (a plain
	/// local debug build) or before OnStartup has run at all.
	/// </summary>
	public AppUpdateCoordinator? UpdateCoordinator => this.updateCoordinator;

	protected override void OnStartup(StartupEventArgs e) {
		this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

		var requestedDirectory = ParseFromHereArgument(e.Args);

		this.singleInstanceMutex = new Mutex(initiallyOwned: true, FromHereProtocol.MutexName, out var createdNew);
		if (!createdNew) {
			// Another instance already owns the mutex - hand off and shut down before base
			// .OnStartup ever gets a chance to create a window (StartupUri="MainWindow.xaml" in
			// App.xaml only fires from within that base call). Never touches updateCoordinator or
			// the gate - this branch exits well before either would matter.
			SendToRunningInstance(requestedDirectory);
			this.singleInstanceMutex.Dispose();
			this.singleInstanceMutex = null;
			this.Shutdown();
			return;
		}

		this.updateCoordinator = AppUpdateCoordinator.CreateForRuntime();

		// Own dedicated thread/Dispatcher (see StartupSplash) - MainWindow doesn't exist yet
		// (StartupUri only creates it once base.OnStartup runs below), so without this the user
		// sees nothing at all for however long the synchronous check/download beneath it takes.
		var splash = new StartupSplash();
		void OnStartupStatusChanged(AppUpdateStatus status) => splash.UpdateStatus(status.Describe());
		this.updateCoordinator.StatusChanged += OnStartupStatusChanged;

		// Runs before the pipe server / from-here install start, and before base.OnStartup
		// creates MainWindow - no reason to spin those up if we're about to exit and relaunch
		// into a newer version anyway.
		startupUpdateGate.BeginCheck();
		try {
			// Task.Run + GetAwaiter().GetResult() (not .Result/.Wait() directly) so no WPF
			// SynchronizationContext continuation gets scheduled back onto this not-yet-pumping
			// thread - avoids the classic "blocking on async in WPF" deadlock.
			var isRestarting = Task.Run(() => this.updateCoordinator.RunStartupCheckAndApplyIfFound(e.Args))
				.GetAwaiter().GetResult();
			if (isRestarting) {
				this.singleInstanceMutex?.Dispose();
				return; // process is already exiting inside RunStartupCheckAndApplyIfFound - the
				// splash is left showing its last status ("Downloading updates") until the process
				// actually exits a moment later.
			}
		}
		catch {
			// Any failure here (offline, feed unreachable, etc.) just means "proceed as a normal launch."
		}
		finally {
			startupUpdateGate.EndCheck();
			this.updateCoordinator.StatusChanged -= OnStartupStatusChanged;
			splash.Close();
		}

		// Posted synchronously, before the dispatcher ever pumps a pipe-driven Post below, so this
		// is always first in FromHereRequests' buffer regardless of when MainWindow attaches. A
		// plain double-click (no --from-here argument) posts nothing - MainWindow's own Show() via
		// StartupUri is enough, matching the pipe path's null-directory case being "come to the
		// foreground" only for an already-running instance, never for a fresh one.
		if (requestedDirectory is not null) {
			this.FromHereRequests.Post(requestedDirectory);
		}

		this.pipeServerCancellation = new CancellationTokenSource();
		_ = this.RunPipeServerAsync(this.pipeServerCancellation.Token);

		// Fire-and-forget: never blocks showing the window. A partial/failed install just means
		// the context menu doesn't work until the next successful launch retries it - see
		// FromHereInstaller's own doc comment.
		_ = Task.Run(FromHereInstaller.Install);

		this.updateCoordinator.StartPeriodicChecks(this.Dispatcher);

		base.OnStartup(e);
	}

	protected override void OnExit(ExitEventArgs e) {
		this.updateCoordinator?.ApplyPendingUpdateOnExit();
		this.pipeServerCancellation?.Cancel();
		this.singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
		// Not attempting to recover from the crash (e.Handled is deliberately left false) - only
		// giving a pending/in-flight update a chance to land first. See HandleCrash.
		HandleCrash(this.updateCoordinator);
	}

	private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
		HandleCrash((Current as App)?.updateCoordinator);
	}

	private static void HandleCrash(AppUpdateCoordinator? coordinator) {
		// First: if a periodic background check already fully downloaded a fix, don't just
		// crash - restart straight into it. Covers any crash, not only ones during the startup
		// check below.
		if (coordinator?.TryApplyPendingUpdateOnCrash() == true) {
			return; // never reached - ApplyUpdatesAndRestart exits the process
		}

		// Otherwise, if the synchronous startup check is still in flight, give it up to
		// CrashGraceMs to conclude - it might itself find+apply a fix and exit before this
		// handler returns.
		startupUpdateGate.WaitForCheckToFinish(CrashGraceMs);
	}

	private static string? ParseFromHereArgument(string[] args) {
		return args.Length == 2 && args[0] == "--from-here" ? args[1] : null;
	}

	private static void SendToRunningInstance(string? directory) {
		try {
			using var client = new NamedPipeClientStream(".", FromHereProtocol.PipeName, PipeDirection.Out);
			client.Connect(2000);

			using var writer = new StreamWriter(client) { AutoFlush = true };
			writer.WriteLine(directory ?? string.Empty);
		}
		catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException) {
			// No UI to report to yet - we haven't shown a window. Matches MultiClod.FromHere's
			// own silent-failure policy for the same send.
		}
	}

	private async Task RunPipeServerAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			try {
				using var server = new NamedPipeServerStream(FromHereProtocol.PipeName, PipeDirection.In, maxNumberOfServerInstances: 1);
				await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

				using var reader = new StreamReader(server);
				var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
				var directory = string.IsNullOrEmpty(line) ? null : line;

				// Marshaled to the UI thread here (NamedPipeServerStream callbacks run on a
				// background thread, same cross-thread discipline this repo already applies to
				// IPtyConnection.OutputReceived/Exited) so FromHereRequestQueue itself can stay
				// UI-free and trivially testable.
				_ = this.Dispatcher.BeginInvoke(() => this.FromHereRequests.Post(directory));
			}
			catch (OperationCanceledException) {
				// Shutting down - exit the loop instead of treating this as a transient error.
				break;
			}
			catch (IOException) {
				// Transient pipe failure - re-enter the accept loop rather than tearing the server
				// down for the rest of the process's life.
			}
		}
	}
}
