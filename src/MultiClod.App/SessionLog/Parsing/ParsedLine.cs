using System.Text.Json;

namespace MultiClod.App.SessionLog.Parsing;

/// <summary>
/// One JSONL transcript line, already detached from its source JsonDocument via
/// JsonElement.Clone() so a row can hold it for its whole lifetime without pinning the document's
/// rented buffer. <see cref="Root"/> is only meaningful when <see cref="IsValidJson"/> is true.
/// </summary>
public sealed record ParsedLine(bool IsValidJson, JsonElement Root, string? TypeValue, string RawText);
