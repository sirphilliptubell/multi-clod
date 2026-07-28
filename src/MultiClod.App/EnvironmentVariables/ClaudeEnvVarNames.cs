namespace MultiClod.App.EnvironmentVariables;

/// <summary>
/// Which env vars count as "Claude Code's" for the session tree's Environment Variables menu, and
/// which of those look secret-like enough to mask in the UI. Prefix/substring-based rather than a
/// fixed enumerated list, so newly added vars (Anthropic adds these fairly often) are picked up
/// automatically instead of silently falling through a stale list - see
/// https://code.claude.com/docs/en/env-vars for the current reference to check this against.
/// </summary>
internal static class ClaudeEnvVarNames
{
    private static readonly string[] Prefixes =
    {
        "ANTHROPIC_",
        "CLAUDE_",
        "CLAUDECODE",
        "BASH_",
    };

    // Exact names that matter but don't share one of the prefixes above.
    private static readonly HashSet<string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AWS_BEARER_TOKEN_BEDROCK",
        "API_TIMEOUT_MS",
        "API_FORCE_IDLE_TIMEOUT",
        "GCLOUD_PROJECT",
        "GOOGLE_CLOUD_PROJECT",
        "GOOGLE_APPLICATION_CREDENTIALS",
        "CCR_FORCE_BUNDLE",
        "DEBUG",
        "ENABLE_TOOL_SEARCH",
    };

    // Deliberately a superset (e.g. this also flags CLAUDE_CODE_CLIENT_KEY, which is actually a
    // cert file path, not a secret value) - masking a harmless path is a fine false positive,
    // silently showing an API key in full because a name pattern missed it is not.
    private static readonly string[] SecretNameSubstrings =
    {
        "KEY",
        "TOKEN",
        "SECRET",
        "AUTH",
        "PASSWORD",
        "PASSPHRASE",
        "CREDENTIAL",
    };

    public static bool IsRelevant(string name) =>
        ExactNames.Contains(name) || Array.Exists(Prefixes, prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsSecretLike(string name) =>
        Array.Exists(SecretNameSubstrings, substring => name.Contains(substring, StringComparison.OrdinalIgnoreCase));
}
