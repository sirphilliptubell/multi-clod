using System.Windows.Threading;

namespace MultiClod.App.Splash;

/// <summary>
/// Shows <see cref="SplashWindow"/> on its own dedicated STA thread with its own Dispatcher, so it
/// stays visible and animated while App.OnStartup blocks the main thread on the synchronous
/// startup update check. A window shown directly on that thread wouldn't render or animate at all
/// during that wait - nothing pumps its Dispatcher while Task.Run(...).GetAwaiter().GetResult() is
/// blocked.
/// </summary>
public sealed class StartupSplash {
	private readonly Thread thread;
	private SplashWindow? window;
	private Dispatcher? dispatcher;

	public StartupSplash() {
		using var ready = new ManualResetEventSlim();

		this.thread = new Thread(() => this.RunSplashThread(ready)) { IsBackground = true };
		this.thread.SetApartmentState(ApartmentState.STA);
		this.thread.Start();

		ready.Wait();
	}

	private void RunSplashThread(ManualResetEventSlim ready) {
		this.window = new SplashWindow();
		this.window.Show();

		// Assigned before signaling ready, so it's always safe to read from the constructing
		// thread the moment ready.Wait() returns.
		this.dispatcher = this.window.Dispatcher;
		ready.Set();

		Dispatcher.Run();
	}

	public void UpdateStatus(string text) {
		this.dispatcher?.BeginInvoke(() => this.window?.SetStatusText(text));
	}

	public void Close() {
		var dispatcher = this.dispatcher;
		if (dispatcher is null) {
			return;
		}

		dispatcher.Invoke(() => {
			this.window?.Close();
			dispatcher.InvokeShutdown();
		});

		this.thread.Join(TimeSpan.FromSeconds(2));
	}
}
