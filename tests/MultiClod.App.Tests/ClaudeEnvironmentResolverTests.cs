using System.Text.Json;
using MultiClod.App.EnvironmentVariables;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeEnvironmentResolverTests
{
    [Test]
    public async Task Resolve_ProjectSettingsEnv_IsIncludedWithProjectSettingsSource()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            WriteSettings(Path.Combine(workingDirectory, ".claude", "settings.json"), new { FOO = "project-value" });

            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            var foo = result.Single(v => v.Key == "FOO");
            await Assert.That(foo.Value).IsEqualTo("project-value");
            await Assert.That(foo.Source).IsEqualTo(EnvironmentVariableSource.ProjectSettings);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Resolve_ProjectLocalOverridesProjectSettings()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            WriteSettings(Path.Combine(workingDirectory, ".claude", "settings.json"), new { FOO = "project-value" });
            WriteSettings(Path.Combine(workingDirectory, ".claude", "settings.local.json"), new { FOO = "local-value" });

            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            var foo = result.Single(v => v.Key == "FOO");
            await Assert.That(foo.Value).IsEqualTo("local-value");
            await Assert.That(foo.Source).IsEqualTo(EnvironmentVariableSource.ProjectLocalSettings);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Resolve_ProjectSettingsOverridesUserSettings()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            WriteSettings(userSettingsPath, new { FOO = "user-value" });
            WriteSettings(Path.Combine(workingDirectory, ".claude", "settings.json"), new { FOO = "project-value" });

            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            var foo = result.Single(v => v.Key == "FOO");
            await Assert.That(foo.Value).IsEqualTo("project-value");
            await Assert.That(foo.Source).IsEqualTo(EnvironmentVariableSource.ProjectSettings);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    // A settings file's "env" block can name literally anything, unlike the OS-environment scan
    // (see ClaudeEnvVarNamesTests) - HTTP_PROXY doesn't match any known Claude prefix but should
    // still surface, since it's explicit config Claude Code itself would apply.
    [Test]
    public async Task Resolve_NonPrefixedNameInSettingsEnv_IsStillIncluded()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            WriteSettings(Path.Combine(workingDirectory, ".claude", "settings.json"), new { HTTP_PROXY = "http://proxy.example" });

            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            await Assert.That(result.Any(v => v.Key == "HTTP_PROXY")).IsTrue();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Resolve_MalformedProjectSettingsFile_IsTreatedAsAbsent()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, ".claude", "settings.json"), "{ not valid json");
            WriteSettings(userSettingsPath, new { FOO = "user-value" });

            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            var foo = result.Single(v => v.Key == "FOO");
            await Assert.That(foo.Value).IsEqualTo("user-value");
            await Assert.That(foo.Source).IsEqualTo(EnvironmentVariableSource.UserSettings);
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    [Test]
    public async Task Resolve_NoSettingsFilesPresent_ReportsNothingFromThem()
    {
        var (root, workingDirectory, userSettingsPath) = CreateScratchLayout();
        try
        {
            var result = ClaudeEnvironmentResolver.Resolve(workingDirectory, userSettingsPath);

            await Assert.That(result.Any(v => v.Key == "FOO")).IsFalse();
        }
        finally
        {
            DeleteScratchDirectory(root);
        }
    }

    private static (string Root, string WorkingDirectory, string UserSettingsPath) CreateScratchLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", Guid.NewGuid().ToString());
        var workingDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(Path.Combine(workingDirectory, ".claude"));
        var userSettingsPath = Path.Combine(root, "user-settings.json");
        return (root, workingDirectory, userSettingsPath);
    }

    private static void WriteSettings(string path, object envValues)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { env = envValues }));
    }

    private static void DeleteScratchDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
