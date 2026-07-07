using System.Runtime.CompilerServices;
using System.Windows;

// Lets MultiClod.App.Tests exercise internal validation logic (ClaudeProjectPath encoding is
// exactly the kind of string-manipulation code worth pinning with a test - see Phase 00's
// SessionStore tests for the same reasoning) without making it part of the public API surface.
[assembly: InternalsVisibleTo("MultiClod.App.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
