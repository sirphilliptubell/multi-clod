using MultiClod.App.Validation;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class SessionValidatorTests
{
    [Test]
    public async Task Validate_MissingDirectory_ReturnsWorkingDirectoryMissing()
    {
        var node = new SessionNodeViewModel(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test",
            Path.Combine(Path.GetTempPath(), "MultiClod.App.Tests", "does-not-exist", Guid.NewGuid().ToString()),
            hasBeenStarted: false);

        var problem = SessionValidator.Validate(node);

        await Assert.That(problem).IsEqualTo(SessionValidationProblem.WorkingDirectoryMissing);
    }

    [Test]
    public async Task Validate_ValidDirectoryNeverStarted_ReturnsNone()
    {
        // A never-started node has no claude-side data yet by definition, so the second check is
        // skipped entirely - only the directory needs to exist.
        var node = new SessionNodeViewModel(Guid.NewGuid(), Guid.NewGuid(), "test", Path.GetTempPath(), hasBeenStarted: false);

        var problem = SessionValidator.Validate(node);

        await Assert.That(problem).IsEqualTo(SessionValidationProblem.None);
    }

    [Test]
    public async Task Validate_ValidDirectoryStartedButNoClaudeData_ReturnsClaudeDataMissing()
    {
        // A freshly random ClaudeSessionId practically can't already have a matching .jsonl under
        // the real ~/.claude/projects, so this deterministically exercises the "started but no
        // transcript" branch without writing anything into the user's real .claude directory.
        var node = new SessionNodeViewModel(Guid.NewGuid(), Guid.NewGuid(), "test", Path.GetTempPath(), hasBeenStarted: true);

        var problem = SessionValidator.Validate(node);

        await Assert.That(problem).IsEqualTo(SessionValidationProblem.ClaudeDataMissing);
    }
}
