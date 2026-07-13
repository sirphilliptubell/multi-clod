<#
    Invoked as a Claude Code hook command (see ClaudeSessionHooksInstaller) for sessions launched
    by multi-clod. Emits a terminalSequence that sets the window title (OSC 2) to a sentinel-
    prefixed marker - TerminalSession.OnHostTitleChanged recognizes the prefix and routes it to
    Activity instead of DetectedTitle, rather than this needing its own escape-sequence channel.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Marker
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
}

$esc = [char]27
$bel = [char]7
$suffix = if ($promptId) { "${Marker}:${promptId}" } else { $Marker }
$sessionSequence = if ($sessionId) { "$esc]2;MULTICLOD_SESSION:$sessionId$bel" } else { "" }
$activitySequence = "$esc]2;MULTICLOD_ACTIVITY:$suffix$bel"

@{ terminalSequence = "$sessionSequence$activitySequence" } | ConvertTo-Json -Compress
