namespace MultiClod.App.EnvironmentVariables;

/// <summary>
/// One resolved env var for the Environment Variables session menu: its final (merged) value and
/// which source won it - see <see cref="ClaudeEnvironmentResolver"/>.
/// </summary>
public sealed record ClaudeEnvironmentVariable(string Key, string Value, EnvironmentVariableSource Source)
{
    public bool IsSecretLike => ClaudeEnvVarNames.IsSecretLike(this.Key);
}
