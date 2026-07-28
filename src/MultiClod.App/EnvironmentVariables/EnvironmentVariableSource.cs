namespace MultiClod.App.EnvironmentVariables;

/// <summary>
/// Where a captured env var's resolved value ultimately came from, in ascending precedence -
/// mirrors Claude Code's own settings precedence (project local overrides project, which overrides
/// user), with the OS-inherited environment as the base anything else can override. Enterprise
/// managed policy is deliberately not modeled here - see the plan's decision to skip it.
/// </summary>
public enum EnvironmentVariableSource
{
    OsEnvironment,
    UserSettings,
    ProjectSettings,
    ProjectLocalSettings,
}

internal static class EnvironmentVariableSourceExtensions
{
    public static string Describe(this EnvironmentVariableSource source) => source switch
    {
        EnvironmentVariableSource.OsEnvironment => "OS environment",
        EnvironmentVariableSource.UserSettings => "user ~/.claude/settings.json",
        EnvironmentVariableSource.ProjectSettings => "project .claude/settings.json",
        EnvironmentVariableSource.ProjectLocalSettings => "project .claude/settings.local.json",
        _ => "unknown",
    };
}
