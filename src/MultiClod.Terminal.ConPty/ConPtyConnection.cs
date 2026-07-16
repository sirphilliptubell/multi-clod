using System.Diagnostics;
using System.Text;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.Terminal.ConPty;

/// <summary>
/// Spawns a child process attached to a Win32 pseudoconsole and adapts it to <see cref="IPtyConnection"/>.
/// The pipe/process plumbing is ported from Microsoft's own samples/ConPTY/MiniTerm, which drives a
/// blocking console loop; this adapts that into the event-driven shape ITerminalPane implementations
/// need, and adds resize support, working-directory support, and process-exit notification.
/// </summary>
public sealed class ConPtyConnection : IPtyConnection
{
    private readonly TerminalLaunchOptions options;
    private readonly object disposeLock = new();

    private PseudoConsolePipe? inputPipe;
    private PseudoConsolePipe? outputPipe;
    private PseudoConsole? pseudoConsole;
    private Win32Process? win32Process;
    private Process? process;
    private StreamWriter? inputWriter;
    private FileStream? outputStream;
    private CancellationTokenSource? pumpCancellation;
    private Task? pumpTask;
    private bool disposed;
    private bool started;

    // Holds an in-progress, not-yet-terminated OSC 0/2 title sequence (starting from its "ESC ]"
    // introducer) across PumpOutput reads, since a 4096-byte read can split a sequence anywhere.
    // Capped defensively so a child process that never terminates a sequence can't grow this
    // unbounded.
    private string? pendingTitleSequence;

    private const int MaxPendingTitleSequenceLength = 4096;

    // Rolling tail of raw output, handed to Exited via ProcessExitedEventArgs.OutputTail - lets a
    // caller see whatever the child printed right before dying (e.g. "'claude' is not recognized
    // as an internal or external command") without needing to catch it live in the terminal pane
    // before it scrolls off or the session/window closes. Guarded by outputTailLock since
    // PumpOutput (a background thread) appends to it while OnProcessExited (a ThreadPool callback
    // from Process.Exited) reads it - StringBuilder itself isn't thread-safe.
    private readonly StringBuilder outputTail = new();
    private readonly object outputTailLock = new();
    private const int OutputTailMaxLength = 4000;

    public ConPtyConnection(TerminalLaunchOptions options)
    {
        this.options = options;
    }

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<ProcessExitedEventArgs>? Exited;

    public event EventHandler<string>? TitleChanged;

    public void Start()
    {
        if (this.started)
        {
            // Spawning twice would leak the first process/pseudoconsole silently (nothing would
            // reference it once these fields get overwritten below) - fail loudly instead.
            throw new InvalidOperationException("ConPtyConnection.Start() was already called.");
        }

        this.started = true;

        this.inputPipe = new PseudoConsolePipe();
        this.outputPipe = new PseudoConsolePipe();
        this.pseudoConsole = PseudoConsole.Create(this.inputPipe.ReadSide, this.outputPipe.WriteSide, (int)this.options.InitialColumns, (int)this.options.InitialRows);
        this.win32Process = ProcessFactory.Start(this.options.CommandLine, this.options.WorkingDirectory, PseudoConsole.PseudoConsoleThreadAttribute, this.pseudoConsole.Handle);

        this.inputWriter = new StreamWriter(new FileStream(this.inputPipe.WriteSide, FileAccess.Write)) { AutoFlush = true };
        this.outputStream = new FileStream(this.outputPipe.ReadSide, FileAccess.Read);

        // Wrapping the raw PID in a managed Process gets us Exited/ExitCode for free instead of
        // hand-rolling a wait handle and a GetExitCodeProcess P/Invoke.
        this.process = Process.GetProcessById(this.win32Process.ProcessInfo.dwProcessId);
        this.process.EnableRaisingEvents = true;
        this.process.Exited += this.OnProcessExited;

        this.pumpCancellation = new CancellationTokenSource();
        this.pumpTask = Task.Run(() => this.PumpOutput(this.pumpCancellation.Token));
    }

    public void WriteInput(string data)
    {
        try
        {
            this.inputWriter?.Write(data);
        }
        catch (IOException)
        {
            // Input pipe already closed (process exited) - nothing to write to.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Resize(uint rows, uint columns)
    {
        // Cheap best-effort guard, same rationale as PseudoConsole.Resize's own disposed check -
        // a resize racing Dispose() (which can run on a background thread - see
        // MainWindow.OnClosing's concurrent Task.Run(Dispose) - while this is called from the UI
        // thread) is expected, not an error.
        if (this.disposed)
        {
            return;
        }

        this.pseudoConsole?.Resize((int)columns, (int)rows);
    }

    public void Dispose()
    {
        lock (this.disposeLock)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
        }

        // Close stdin first so a well-behaved child (most CLIs, including node-based ones) sees
        // EOF and can exit on its own before we resort to killing it.
        try
        {
            this.inputWriter?.Dispose();
        }
        catch (IOException)
        {
        }

        if (this.process is { HasExited: false } liveProcess)
        {
            if (!liveProcess.WaitForExit(1500))
            {
                liveProcess.Kill(entireProcessTree: true);
            }
        }

        if (this.process is not null)
        {
            this.process.Exited -= this.OnProcessExited;
        }

        // Closing the pseudoconsole (rather than cancelling the pump immediately) lets any output
        // the child already flushed drain through the pipe as a graceful EOF - cancelling first
        // races the pump against still-buffered output and can silently truncate the tail of it.
        this.pseudoConsole?.Dispose();

        try
        {
            this.pumpTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // Pump task's own exception handling already covers expected shutdown exceptions.
        }

        this.pumpCancellation?.Cancel();

        this.outputStream?.Dispose();
        this.process?.Dispose();
        this.win32Process?.Dispose();
        this.inputPipe?.Dispose();
        this.outputPipe?.Dispose();
        this.pumpCancellation?.Dispose();
    }

    private void PumpOutput(CancellationToken cancellationToken)
    {
        // A blocking synchronous Read on a dedicated background thread, not ReadAsync - the pipe
        // handle from CreatePipe is not opened for overlapped I/O, and mirrors the synchronous
        // Read/CopyTo approach MiniTerm itself uses against the same kind of handle.
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = this.outputStream!.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                this.OutputReceived?.Invoke(this, text);
                this.ScanForTitleSequences(text);
                this.AppendToOutputTail(text);
            }
        }
        catch (IOException)
        {
            // Pipe broke because the child process exited / pseudoconsole closed - not an error.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Only observes the stream for OSC 0 ("icon name + window title") and OSC 2 ("window title")
    // sequences - it never strips or rewrites what's already been forwarded via OutputReceived,
    // since the native Terminal control consumes/strips OSC sequences itself for display.
    private void ScanForTitleSequences(string text)
    {
        var combined = this.pendingTitleSequence is null ? text : this.pendingTitleSequence + text;
        this.pendingTitleSequence = null;

        var searchStart = 0;
        while (true)
        {
            var oscStart = combined.IndexOf("\x1b]", searchStart, StringComparison.Ordinal);
            if (oscStart < 0)
            {
                return;
            }

            var kindStart = oscStart + 2;
            if (kindStart + 1 >= combined.Length)
            {
                // Not enough data yet to tell whether this is a "0;"/"2;" title introducer - hold
                // it for the next read.
                this.SetPendingTitleSequence(combined[oscStart..]);
                return;
            }

            var kind = combined.Substring(kindStart, 2);
            if (kind != "0;" && kind != "2;")
            {
                searchStart = kindStart;
                continue;
            }

            var contentStart = kindStart + 2;
            var belIndex = combined.IndexOf('\x07', contentStart);
            var stIndex = combined.IndexOf("\x1b\\", contentStart, StringComparison.Ordinal);

            int terminatorIndex;
            int terminatorLength;
            if (belIndex >= 0 && (stIndex < 0 || belIndex < stIndex))
            {
                (terminatorIndex, terminatorLength) = (belIndex, 1);
            }
            else if (stIndex >= 0)
            {
                (terminatorIndex, terminatorLength) = (stIndex, 2);
            }
            else
            {
                // Sequence hasn't terminated within this chunk yet - carry the whole thing forward.
                this.SetPendingTitleSequence(combined[oscStart..]);
                return;
            }

            var title = combined.Substring(contentStart, terminatorIndex - contentStart);
            this.TitleChanged?.Invoke(this, title);
            searchStart = terminatorIndex + terminatorLength;
        }
    }

    private void SetPendingTitleSequence(string value)
    {
        this.pendingTitleSequence = value.Length <= MaxPendingTitleSequenceLength ? value : null;
    }

    private void AppendToOutputTail(string text)
    {
        lock (this.outputTailLock)
        {
            this.outputTail.Append(text);

            var excess = this.outputTail.Length - OutputTailMaxLength;
            if (excess > 0)
            {
                this.outputTail.Remove(0, excess);
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = 0;
        try
        {
            exitCode = this.process?.ExitCode ?? 0;
        }
        catch (InvalidOperationException)
        {
            // Process handle already torn down by Dispose().
        }

        string outputTail;
        lock (this.outputTailLock)
        {
            outputTail = this.outputTail.ToString();
        }

        this.Exited?.Invoke(this, new ProcessExitedEventArgs(exitCode, outputTail));
    }
}
