namespace MultiClod.App.FromHere;

/// <summary>
/// Buffers from-here requests (a directory to create/select a session for, or null to just come
/// to the foreground) until a handler is attached, then routes them directly. This is the single
/// seam both delivery paths - this process's own --from-here startup argument, and a later
/// hand-off over the named pipe from a fresh MultiClod.FromHere invocation - post through, so
/// neither one can race MainWindow's construction the way a plain event once did (a request
/// raised before anything subscribes is simply lost). UI-free and takes no dependency on
/// Dispatcher: callers on a background thread (the pipe server) are responsible for marshaling to
/// the UI thread before calling <see cref="Post"/>, same as they would have for a raised event.
/// </summary>
public sealed class FromHereRequestQueue
{
    private readonly object gate = new();
    private readonly List<string?> buffered = new();
    private Action<string?>? handler;

    /// <summary>
    /// Posts a request. Buffered (FIFO) until <see cref="Attach"/> is called; delivered
    /// immediately thereafter, until a subsequent <see cref="Detach"/> starts buffering again.
    /// </summary>
    public void Post(string? directory)
    {
        Action<string?>? current;
        lock (this.gate)
        {
            current = this.handler;
            if (current is null)
            {
                this.buffered.Add(directory);
                return;
            }
        }

        current(directory);
    }

    /// <summary>
    /// Attaches the handler and immediately drains anything buffered, oldest first - e.g. this
    /// process's own startup directory (posted from OnStartup, before MainWindow exists) is always
    /// first in the buffer, since it's posted synchronously before the dispatcher ever pumps a
    /// pipe-driven Post.
    /// </summary>
    public void Attach(Action<string?> handler)
    {
        List<string?> toDeliver;
        lock (this.gate)
        {
            this.handler = handler;
            toDeliver = new List<string?>(this.buffered);
            this.buffered.Clear();
        }

        foreach (var directory in toDeliver)
        {
            handler(directory);
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
