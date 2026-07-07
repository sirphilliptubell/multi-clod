namespace MultiClod.Shared;

/// <summary>
/// Constants shared between MultiClod.App and the MultiClod.FromHere stub. Linked into both
/// projects' compilation (not referenced as an assembly) so the stub never has to depend on the
/// WPF app - see MultiClod.App.csproj's comment on the ProjectReference to MultiClod.FromHere for
/// why that direction is forbidden. internal (not public): MultiClod.App also has a
/// ProjectReference to MultiClod.FromHere (solely to copy its build output - see that csproj's own
/// comment), which would otherwise re-expose FromHere's compiled copy of this same public type
/// into MultiClod.App's compilation alongside its own linked copy (CS0436). Each assembly using
/// its own internal copy avoids the clash entirely.
/// </summary>
internal static class FromHereProtocol
{
#if DEBUG
    public const string MutexName = "MultiClod.SingleInstance.Debug";

    public const string PipeName = "MultiClod.FromHerePipe.Debug";
#else
    public const string MutexName = "MultiClod.SingleInstance";

    public const string PipeName = "MultiClod.FromHerePipe";
#endif

    /// <summary>
    /// The app's dotfile-style data root directory name under the user's profile, e.g.
    /// ~/.multi-clod - deliberately not %LOCALAPPDATA%, to match ~/.claude itself. Debug builds
    /// use a separate ~/.multi-clod-debug root (alongside their own Mutex/PipeName above) so a
    /// locally-built Debug copy can run side-by-side with a stable Release copy with zero
    /// file/pipe/registry contention between them.
    /// </summary>
#if DEBUG
    public const string DataDirectoryName = ".multi-clod-debug";
#else
    public const string DataDirectoryName = ".multi-clod";
#endif

    public const string ConfigFileName = "from-here-config.json";
}
