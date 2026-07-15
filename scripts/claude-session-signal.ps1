<#
    Invoked as a Claude Code hook command (see ClaudeSessionHooksInstaller) for sessions launched
    by multi-clod. Emits a terminalSequence that sets the window title (OSC 2) to a single sentinel-
    prefixed marker packing both the activity kind and the live session_id - TerminalSession.ApplyTitle
    recognizes the prefix and routes it to Activity/ObservedClaudeSessionId instead of DetectedTitle,
    rather than this needing its own escape-sequence channel. Deliberately ONE OSC 2 sequence, not
    two independent ones concatenated - live testing showed Claude Code only forwards the last
    title-setting escape sequence from a hook's terminalSequence to the actual terminal output when
    more than one is present, silently dropping the first. See TerminalSession's CombinedSentinelPrefix.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Marker,

    # Only ever passed by ClaudeSessionHooksInstaller in Debug builds (see its #if DEBUG) - a
    # temporary aid for diagnosing why the session_id sync (below) isn't correcting drift after
    # /clear. Remove once that's root-caused; Release builds never see this param, so it's always
    # empty there and this whole block is a no-op.
    [string]$DebugLogPath
)

# Claude Code pipes the hook's JSON payload in on stdin. prompt_id lets TerminalSession match a
# Stop hook back to the exact turn that set a sticky NeedsInput, rather than trusting the relative
# arrival order of two independently-spawned hook processes - see TerminalSession.OnHostTitleChanged.
# session_id is Claude Code's own live conversation id - it changes underneath us if the user runs
# /clear (or /resume to a different conversation) inside the CLI, so re-reporting it on every hook
# lets TerminalSession/SessionNodeViewModel notice the drift and re-persist the corrected id, rather
# than multi-clod's stale --session-id/--resume guid resuming an abandoned conversation.
$stdin = [Console]::In.ReadToEnd()
$promptId = $null
$sessionId = $null
$parseError = $null
try
{
    $payload = $stdin | ConvertFrom-Json -ErrorAction Stop
    $promptId = $payload.prompt_id
    $sessionId = $payload.session_id
}
catch
{
    # Malformed/missing JSON on stdin - both just stay $null, which is fine for markers that don't
    # need promptId (Working/NeedsInputTransient) and degrades safely for the ones that do; omitting
    # the session-id sequence entirely just means this particular hook firing doesn't correct drift.
    $parseError = $_.Exception.Message
}

if ($DebugLogPath)
{
    try
    {
        $line = "[$(Get-Date -Format o)] Marker=$Marker PID=$PID promptId=$promptId sessionId=$sessionId parseError=$parseError stdin=$stdin"
        Add-Content -Path $DebugLogPath -Value $line -ErrorAction Stop
    }
    catch
    {
        # Best-effort logging only - a locked/missing log file must never break the real hook.
    }
}

$esc = [char]27
$bel = [char]7
$suffix = if ($promptId) { "${Marker}:${promptId}" } else { $Marker }

# session_id and the activity suffix are packed into ONE title as "<sessionId>|<suffix>" rather
# than two separate OSC sequences - see the remarks above. sessionId is a plain GUID (no '|'), so
# TerminalSession can split unambiguously on the first '|'. An empty sessionId (JSON parse failure)
# still round-trips safely as a leading '|'.
$combined = "MULTICLOD:$sessionId|$suffix"

@{ terminalSequence = "$esc]2;$combined$bel" } | ConvertTo-Json -Compress
