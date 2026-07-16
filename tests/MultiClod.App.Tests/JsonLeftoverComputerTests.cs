using System.Text.Json;
using MultiClod.App.SessionLog.Rendering;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MultiClod.App.Tests;

public sealed class JsonLeftoverComputerTests
{
    [Test]
    public async Task ComputeLeftoverJson_TopLevelConsumedKey_IsExcludedEntirely()
    {
        using var doc = JsonDocument.Parse("{\"type\":\"user\",\"uuid\":\"abc\",\"extra\":\"value\"}");

        var leftover = JsonLeftoverComputer.ComputeLeftoverJson(doc.RootElement, new HashSet<string> { "type", "uuid" });

        await Assert.That(leftover).Contains("extra");
        await Assert.That(leftover).DoesNotContain("uuid");
        await Assert.That(leftover).DoesNotContain("\"type\"");
    }

    // The consumed path "message.content" only hides the content array itself - sibling fields
    // under "message" (model, usage) must still surface, per the approved plan's one-level recurse.
    [Test]
    public async Task ComputeLeftoverJson_PartiallyConsumedNestedObject_KeepsSiblingProperties()
    {
        using var doc = JsonDocument.Parse("{\"message\":{\"content\":\"hi\",\"model\":\"claude-x\",\"usage\":{\"tokens\":5}}}");

        var leftover = JsonLeftoverComputer.ComputeLeftoverJson(doc.RootElement, new HashSet<string> { "message.content" });

        await Assert.That(leftover).Contains("model");
        await Assert.That(leftover).Contains("usage");
        await Assert.That(leftover).DoesNotContain("\"content\"");
    }

    [Test]
    public async Task ComputeLeftoverJson_EverythingConsumed_ReturnsEmptyObject()
    {
        using var doc = JsonDocument.Parse("{\"type\":\"user\"}");

        var leftover = JsonLeftoverComputer.ComputeLeftoverJson(doc.RootElement, new HashSet<string> { "type" });

        await Assert.That(leftover).IsEqualTo("{}");
    }

    [Test]
    public async Task ComputeLeftoverJson_NestedObjectFullyConsumed_OmitsWrapperEntirely()
    {
        using var doc = JsonDocument.Parse("{\"message\":{\"content\":\"hi\",\"role\":\"user\"}}");

        var leftover = JsonLeftoverComputer.ComputeLeftoverJson(doc.RootElement, new HashSet<string> { "message.content", "message.role" });

        await Assert.That(leftover).IsEqualTo("{}");
    }
}
