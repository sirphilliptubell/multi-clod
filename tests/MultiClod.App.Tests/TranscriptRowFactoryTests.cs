using MultiClod.App.SessionLog.Rendering;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class TranscriptRowFactoryTests
{
    [Test]
    public async Task ProcessLine_UserPlainStringContent_ReturnsOneMessageTextRow()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"hello there\"}}");

        await Assert.That(rows).Count().IsEqualTo(1);
        var row = (MessageTextRowViewModel)rows[0];
        await Assert.That(row.Category).IsEqualTo(TranscriptRowCategory.User);
        await Assert.That(row.ExpandedBodyText).IsEqualTo("hello there");
    }

    [Test]
    public async Task ProcessLine_MalformedJson_ReturnsUnrecognizedRow()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine("not json at all");

        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0]).IsTypeOf<UnrecognizedRowViewModel>();
        await Assert.That(rows[0].Category).IsEqualTo(TranscriptRowCategory.Unrecognized);
    }

    [Test]
    public async Task ProcessLine_ValidJsonWithNoType_ReturnsUnrecognizedRow()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine("{\"foo\":\"bar\"}");

        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0]).IsTypeOf<UnrecognizedRowViewModel>();
    }

    // Unfamiliar (but syntactically valid) event types must render as SystemMeta, not
    // Unrecognized - Unrecognized is reserved for actual parse failures, per the approved plan.
    [Test]
    public async Task ProcessLine_UnfamiliarTopLevelType_ReturnsSystemMetaRowNotUnrecognized()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine("{\"type\":\"some-brand-new-event-type\"}");

        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0]).IsTypeOf<SystemMetaRowViewModel>();
        await Assert.That(rows[0].Category).IsEqualTo(TranscriptRowCategory.SystemMeta);
    }

    [Test]
    public async Task ProcessLine_ToolUseThenMatchingToolResult_MergesIntoOneRowInPlace()
    {
        var factory = new TranscriptRowFactory();

        var toolUseRows = factory.ProcessLine(
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"tool_1\",\"name\":\"Read\",\"input\":{\"file_path\":\"a.txt\"}}]}}");

        await Assert.That(toolUseRows).Count().IsEqualTo(1);
        var toolCallRow = (ToolCallRowViewModel)toolUseRows[0];
        await Assert.That(toolCallRow.IsPending).IsTrue();

        var toolResultRows = factory.ProcessLine(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
            "{\"type\":\"tool_result\",\"tool_use_id\":\"tool_1\",\"content\":\"file contents\"}]}}");

        // The tool_result line contributes no new row - it mutated the existing pending row.
        await Assert.That(toolResultRows).IsEmpty();
        await Assert.That(toolCallRow.IsPending).IsFalse();
        await Assert.That(toolCallRow.IsError).IsFalse();
        await Assert.That(toolCallRow.ExpandedBodyText).Contains("file contents");
    }

    [Test]
    public async Task ProcessLine_ToolResultWithErrorFlag_MarksRowAsError()
    {
        var factory = new TranscriptRowFactory();
        var toolUseRows = factory.ProcessLine(
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"tool_1\",\"name\":\"Bash\",\"input\":{}}]}}");
        var toolCallRow = (ToolCallRowViewModel)toolUseRows[0];

        factory.ProcessLine(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
            "{\"type\":\"tool_result\",\"tool_use_id\":\"tool_1\",\"is_error\":true,\"content\":\"boom\"}]}}");

        await Assert.That(toolCallRow.IsError).IsTrue();
    }

    [Test]
    public async Task ProcessLine_OrphanToolResult_RendersDegradedRowInsteadOfDroppingData()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
            "{\"type\":\"tool_result\",\"tool_use_id\":\"tool_missing\",\"content\":\"orphan output\"}]}}");

        await Assert.That(rows).Count().IsEqualTo(1);
        var row = (ToolCallRowViewModel)rows[0];
        await Assert.That(row.ToolName).IsEqualTo("(unknown tool)");
        await Assert.That(row.IsPending).IsFalse();
        await Assert.That(row.ExpandedBodyText).Contains("orphan output");
    }

    [Test]
    public async Task ProcessLine_AssistantWithMultipleContentBlocks_ReturnsOneRowPerBlock()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine(
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[" +
            "{\"type\":\"thinking\",\"thinking\":\"pondering\"}," +
            "{\"type\":\"text\",\"text\":\"here you go\"}]}}");

        await Assert.That(rows).Count().IsEqualTo(2);
        await Assert.That(rows[0]).IsTypeOf<ThinkingRowViewModel>();
        await Assert.That(rows[1]).IsTypeOf<MessageTextRowViewModel>();
    }

    [Test]
    public async Task ProcessLine_UnknownContentBlockType_RendersFallbackRowInsteadOfDropping()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine(
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[" +
            "{\"type\":\"some_future_block\",\"value\":1}]}}");

        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0].SummaryText).Contains("some_future_block");
    }

    [Test]
    public async Task ProcessLine_ImageBlock_RendersPlaceholderInParentCategory()
    {
        var factory = new TranscriptRowFactory();

        var rows = factory.ProcessLine(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
            "{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"data\":\"...\"}}]}}");

        await Assert.That(rows).Count().IsEqualTo(1);
        var row = (MessageTextRowViewModel)rows[0];
        await Assert.That(row.Category).IsEqualTo(TranscriptRowCategory.User);
        await Assert.That(row.ExpandedBodyText).IsEqualTo("[image attached]");
    }
}
