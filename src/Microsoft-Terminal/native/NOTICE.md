# Microsoft.Terminal.Control.dll — provenance

`x64/Microsoft.Terminal.Control.dll` is not built from source in this repo. It is extracted from
a locally-installed Windows Terminal Store package, because the upstream native project
(`src/cascadia/TerminalControl` in `microsoft/terminal`) requires the full C++/WinRT
`microsoft/terminal` monorepo toolchain (Visual Studio 2026, Windows SDK 10.0.26100) to build, and
there is no supported public NuGet package that ships this binary (tracked upstream by
[microsoft/terminal#6999](https://github.com/microsoft/terminal/issues/6999)).

- **Source package**: `Microsoft.WindowsTerminal_1.24.11321.0_x64__8wekyb3d8bbwe` (Microsoft Store)
- **File**: `Microsoft.Terminal.Control.dll`
- **SHA256**: `f8be0b4b417414906a1ec2efd6faab01ea7356290fe7ba062fa7adfeef258352`
- **Architecture**: x64 only — the app must build/run as x64, not AnyCPU or x86/ARM64
- **License**: Microsoft Corporation, part of Windows Terminal (MIT-licensed project:
  https://github.com/microsoft/terminal/blob/main/LICENSE)

## Verified exports (via direct PE export-table parse)

```
AvoidBuggyTSFConsoleFlags
CreateTerminal
DestroyTerminal
DllCanUnloadNow
DllGetActivationFactory
TerminalBlinkCursor
TerminalCalculateResize
TerminalClearSelection
TerminalDpiChanged
TerminalGetSelection
TerminalIsSelectionActive
TerminalKillFocus
TerminalRegisterScrollCallback
TerminalRegisterWriteCallback
TerminalSendCharEvent
TerminalSendKeyEvent
TerminalSendOutput
TerminalSetCursorVisible
TerminalSetFocus
TerminalSetTheme
TerminalTriggerResize
TerminalTriggerResizeWithDimension
TerminalUserScroll
```

## Known ABI gap vs. current upstream source

This DLL predates the consolidated `TerminalSetFocused(IntPtr, bool)` export used by current
upstream `WpfTerminalControl` source (the C# we vendor in `../WpfTerminalControl/`). It only
exports the older split `TerminalSetFocus(IntPtr)` / `TerminalKillFocus(IntPtr)` pair. The vendored
`NativeMethods.cs`/`TerminalContainer.cs` were patched to call the split pair instead — see the
comment at the `TerminalSetFocus`/`TerminalKillFocus` P/Invoke declarations.

## Re-vendoring

If this DLL ever needs to be refreshed (e.g. to pick up a bug fix, or because a future Windows
Terminal update breaks something), re-run the export-table check against the new DLL before
swapping it in — the flat C export surface is not a stable, versioned contract, and older/newer
builds may add, remove, or rename exports (as happened with `TerminalSetFocused` above).
