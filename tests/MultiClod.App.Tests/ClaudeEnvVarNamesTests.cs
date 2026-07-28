using MultiClod.App.EnvironmentVariables;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeEnvVarNamesTests
{
    [Test]
    public async Task IsRelevant_KnownPrefix_ReturnsTrue()
    {
        await Assert.That(ClaudeEnvVarNames.IsRelevant("ANTHROPIC_API_KEY")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsRelevant("CLAUDE_CODE_DISABLE_ARTIFACT")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsRelevant("BASH_MAX_TIMEOUT_MS")).IsTrue();
    }

    [Test]
    public async Task IsRelevant_ExactNameWithoutSharedPrefix_ReturnsTrue()
    {
        await Assert.That(ClaudeEnvVarNames.IsRelevant("DEBUG")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsRelevant("GOOGLE_APPLICATION_CREDENTIALS")).IsTrue();
    }

    [Test]
    public async Task IsRelevant_UnrelatedVariable_ReturnsFalse()
    {
        await Assert.That(ClaudeEnvVarNames.IsRelevant("PATH")).IsFalse();
        await Assert.That(ClaudeEnvVarNames.IsRelevant("HTTP_PROXY")).IsFalse();
    }

    [Test]
    public async Task IsRelevant_IsCaseInsensitive()
    {
        await Assert.That(ClaudeEnvVarNames.IsRelevant("anthropic_api_key")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsRelevant("debug")).IsTrue();
    }

    [Test]
    public async Task IsSecretLike_KeyTokenSecretAuthNames_ReturnsTrue()
    {
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("ANTHROPIC_API_KEY")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("ANTHROPIC_AUTH_TOKEN")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("AWS_BEARER_TOKEN_BEDROCK")).IsTrue();
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("CLAUDE_CODE_CLIENT_KEY_PASSPHRASE")).IsTrue();
    }

    // Deliberate over-masking - CLAUDE_CODE_CLIENT_KEY is a cert file path, not a secret value,
    // but the "KEY" substring still flags it. See ClaudeEnvVarNames' remarks: a harmless
    // false-positive mask is preferred over ever missing a real secret.
    [Test]
    public async Task IsSecretLike_NonSecretPathWithKeySubstring_StillReturnsTrue()
    {
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("CLAUDE_CODE_CLIENT_KEY")).IsTrue();
    }

    [Test]
    public async Task IsSecretLike_OrdinaryVariable_ReturnsFalse()
    {
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("ANTHROPIC_MODEL")).IsFalse();
        await Assert.That(ClaudeEnvVarNames.IsSecretLike("CLAUDE_CODE_DEBUG_LOGS_DIR")).IsFalse();
    }
}
