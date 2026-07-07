using MultiClod.App.Validation;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class ClaudeProjectPathTests
{
    [Test]
    public async Task Encode_MatchesClaudeCliConvention()
    {
        var encoded = ClaudeProjectPath.Encode(@"C:\_Gits-GS-Github\multi-claude");

        await Assert.That(encoded).IsEqualTo("C---Gits-GS-Github-multi-claude");
    }

    [Test]
    public async Task Encode_LowercasesDriveLetterInput_StillUppercasesIt()
    {
        var encoded = ClaudeProjectPath.Encode(@"c:\Users\ptubell\Documents");

        await Assert.That(encoded).StartsWith("C-");
    }

    [Test]
    public async Task GetSessionFilePath_EndsWithSessionIdJsonl()
    {
        var sessionId = Guid.NewGuid();
        var path = ClaudeProjectPath.GetSessionFilePath(@"C:\_Gits-GS-Github\multi-claude", sessionId);

        await Assert.That(path).EndsWith($"{sessionId}.jsonl");
        await Assert.That(path).Contains("C---Gits-GS-Github-multi-claude");
    }
}
