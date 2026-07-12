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
$stdin = [Console]::In.ReadToEnd()
$promptId = $null
try
{
    $promptId = ($stdin | ConvertFrom-Json -ErrorAction Stop).prompt_id
}
catch
{
    # Malformed/missing JSON on stdin - promptId just stays $null, which is fine for markers that
    # don't need it (Working/NeedsInputTransient) and degrades safely for the ones that do.
}

$esc = [char]27
$bel = [char]7
$suffix = if ($promptId) { "${Marker}:${promptId}" } else { $Marker }
$sequence = "$esc]2;MULTICLOD_ACTIVITY:$suffix$bel"

@{ terminalSequence = $sequence } | ConvertTo-Json -Compress
