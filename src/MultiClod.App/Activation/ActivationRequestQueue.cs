namespace MultiClod.App.Activation;

/// <summary>
/// Buffers activation requests (a from-here directory, a deeplink source, or null to just come to
/// the foreground) until a handler is attached, then routes them directly. This is the single seam
/// every delivery path - this process's own startup arguments, and a later hand-off over the named
/// pipe from a fresh MultiClod.App invocation - post through, so none of them can race MainWindow's
/// construction the way a plain event once did (a request raised before anything subscribes is
/// simply lost). UI-free and takes no dependency on Dispatcher: callers on a background thread (the
/// pipe server) are responsible for marshaling to the UI thread before calling <see cref="Post"/>,
/// same as they would have for a raised event.
/// </summary>
public sealed class ActivationRequestQueue
{
    private readonly object gate = new();
    private readonly List<ActivationRequest?> buffered = new();
    private Action<ActivationRequest?>? handler;

    /// <summary>
    /// Posts a request. Buffered (FIFO) until <see cref="Attach"/> is called; delivered
    /// immediately thereafter, until a subsequent <see cref="Detach"/> starts buffering again.
    /// </summary>
    public void Post(ActivationRequest? request)
    {
        Action<ActivationRequest?>? current;
        lock (this.gate)
        {
            current = this.handler;
            if (current is null)
            {
                this.buffered.Add(request);
                return;
            }
        }

        current(request);
    }

    /// <summary>
    /// Attaches the handler and immediately drains anything buffered, oldest first - e.g. this
    /// process's own startup request (posted from OnStartup, before MainWindow exists) is always
    /// first in the buffer, since it's posted synchronously before the dispatcher ever pumps a
    /// pipe-driven Post.
    /// </summary>
    public void Attach(Action<ActivationRequest?> handler)
    {
        List<ActivationRequest?> toDeliver;
        lock (this.gate)
        {
            this.handler = handler;
            toDeliver = new List<ActivationRequest?>(this.buffered);
            this.buffered.Clear();
        }

        foreach (var request in toDeliver)
        {
            handler(request);
        }
    }

    /// <summary>
    /// Detaches the handler - e.g. MainWindow.OnClosing, so a request that lands after the window
    /// starts closing re-buffers instead of driving a closed window (which would throw) rather
    /// than being silently dropped.
    /// </summary>
    public void Detach()
    {
        lock (this.gate)
        {
            this.handler = null;
        }
    }
}
