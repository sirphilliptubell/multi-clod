namespace MultiClod.App.Updates;

/// <summary>
/// Signaled ("safe to crash") except during the narrow window where the synchronous startup
/// update check is actually in flight. Wraps the underlying ManualResetEventSlim behind named
/// operations rather than leaving a raw sync primitive in App.xaml.cs, and gives the
/// crash-resilience wait behavior a seam that's testable on its own.
/// </summary>
public sealed class StartupUpdateGate {
	private readonly ManualResetEventSlim gate = new(initialState: true);

	public void BeginCheck() => this.gate.Reset();

	public void EndCheck() => this.gate.Set();

	public void WaitForCheckToFinish(int timeoutMs) => this.gate.Wait(timeoutMs);
}
