using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Computes the "Additional Properties" JSON for a row: every property on a raw transcript-line
/// JsonElement that its row's typed rendering didn't otherwise surface. Consumed paths use dotted
/// notation (e.g. "message.content") to mean "only that one sub-property of the compound field is
/// rendered elsewhere" - the object is recursed exactly one level so sibling sub-properties (e.g.
/// message.model, message.usage) still surface instead of disappearing just because "message" as a
/// whole is mentioned. This algorithm has no per-type knowledge - only the ConsumedPaths a row
/// declares is type-specific.
/// </summary>
internal static class JsonLeftoverComputer
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static string ComputeLeftoverJson(JsonElement root, IReadOnlySet<string> consumedPaths)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "{}";
        }

        var leftover = new JsonObject();
        foreach (var property in root.EnumerateObject())
        {
            if (consumedPaths.Contains(property.Name))
            {
                continue;
            }

            var nestedLeftover = TryComputeNestedLeftover(property, consumedPaths);
            if (nestedLeftover is not null)
            {
                if (nestedLeftover.Count > 0)
                {
                    leftover[property.Name] = nestedLeftover;
                }

                continue;
            }

            leftover[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        return leftover.Count == 0 ? "{}" : leftover.ToJsonString(IndentedOptions);
    }

    private static JsonObject? TryComputeNestedLeftover(JsonProperty property, IReadOnlySet<string> consumedPaths)
    {
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prefix = property.Name + ".";
        var hasPartialConsumption = consumedPaths.Any(p => p.StartsWith(prefix, StringComparison.Ordinal));
        if (!hasPartialConsumption)
        {
            return null;
        }

        var nested = new JsonObject();
        foreach (var nestedProperty in property.Value.EnumerateObject())
        {
            if (!consumedPaths.Contains(prefix + nestedProperty.Name))
            {
                nested[nestedProperty.Name] = JsonNode.Parse(nestedProperty.Value.GetRawText());
            }
        }

        return nested;
    }
}
