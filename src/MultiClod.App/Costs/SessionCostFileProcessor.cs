using System.IO;
using System.Text;
using MultiClod.App.SessionLog.Parsing;

namespace MultiClod.App.Costs;

/// <summary>
/// Stateless per-file reparse routine: given one JSONL log path, decides (via its sidecar) whether
/// anything changed since last check, reads only the new bytes if so, and returns/persists an
/// updated per-model-slug cost dictionary. No state is carried between calls - everything needed is
/// either the source file itself or its sidecar, so this can be called from any thread at any time
/// with no setup.
/// </summary>
internal static class SessionCostFileProcessor
{
    public static IReadOnlyDictionary<string, decimal?> ProcessFile(string jsonlPath)
    {
        if (!File.Exists(jsonlPath))
        {
            return new Dictionary<string, decimal?>();
        }

        var sidecarPath = SessionCostCacheFile.SidecarPathFor(jsonlPath);
        var sidecar = SessionCostCacheFile.TryLoad(sidecarPath);

        var fileInfo = new FileInfo(jsonlPath);
        var currentLength = fileInfo.Length;
        var currentMtimeUtc = fileInfo.LastWriteTimeUtc;

        // A model previously marked unknown might now have a rate (e.g. mc-update-costs just ran)
        // - if so, force one full reparse so this file gets correctly priced instead of staying
        // stuck at null forever.
        var needsSelfHeal = sidecar is not null
            && sidecar.ModelCostsUsd.Any(kvp => kvp.Value is null && ClaudeModelPricing.HasAnyRateFor(kvp.Key));

        long startOffset;
        Dictionary<string, decimal?> costs;

        if (needsSelfHeal || sidecar is null || currentLength < sidecar.LastReadOffset)
        {
            // Full reparse: brand-new sidecar, the source file shrank/was replaced (Claude Code can
            // swap the underlying transcript file mid-session, e.g. on /clear), or a self-heal.
            startOffset = 0;
            costs = new Dictionary<string, decimal?>();
        }
        else if (currentMtimeUtc == sidecar.SourceLastWriteUtc)
        {
            // Nothing changed since last check - a stat call was enough, no need to open the file.
            return sidecar.ModelCostsUsd;
        }
        else
        {
            startOffset = sidecar.LastReadOffset;
            costs = new Dictionary<string, decimal?>(sidecar.ModelCostsUsd);
        }

        long newOffset;
        try
        {
            newOffset = ReadAndAccumulate(jsonlPath, startOffset, costs);
        }
        catch (IOException)
        {
            // The `claude` CLI process may hold a conflicting lock - treat as "no change this
            // check" rather than faulting; the sidecar is left untouched so the next scheduled
            // check (debounce/backstop) simply retries.
            return sidecar?.ModelCostsUsd ?? costs;
        }

        var updated = new SessionCostCacheFile
        {
            LastReadOffset = newOffset,
            SourceLastWriteUtc = currentMtimeUtc,
            ModelCostsUsd = costs,
        };
        SessionCostCacheFile.Save(sidecarPath, updated);

        return costs;
    }

    // Mirrors TranscriptFileTailer.ExtractCompleteLines' byte-level line splitting (safe regardless
    // of encoding, no BOM-detection surprises) - just one-shot instead of buffered across calls,
    // since this routine carries no state between invocations. A trailing torn (non-'\n'-terminated)
    // line is left unconsumed; the returned offset stops right before it so the next check re-reads
    // it in full once the writer finishes the line.
    private static long ReadAndAccumulate(string jsonlPath, long startOffset, Dictionary<string, decimal?> costs)
    {
        using var stream = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(startOffset, SeekOrigin.Begin);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var lineStart = 0;
        var consumedBytes = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
            {
                continue;
            }

            var length = i - lineStart;
            if (length > 0 && bytes[lineStart + length - 1] == (byte)'\r')
            {
                length--;
            }

            if (length > 0)
            {
                ProcessLine(Encoding.UTF8.GetString(bytes, lineStart, length), costs);
            }

            lineStart = i + 1;
            consumedBytes = lineStart;
        }

        return startOffset + consumedBytes;
    }

    // A line that fails to parse as JSON, or isn't an "assistant" line, or has no usable
    // message.usage/model, contributes nothing and never faults the file - same posture as
    // TranscriptLineParser's own handling of lines it doesn't recognize.
    private static void ProcessLine(string rawLine, Dictionary<string, decimal?> costs)
    {
        var parsed = TranscriptLineParser.Parse(rawLine);
        if (!parsed.IsValidJson || parsed.TypeValue != "assistant")
        {
            return;
        }

        if (ClaudeUsageReader.TryRead(parsed.Root) is not { } usageLine || usageLine.ModelSlug.Length == 0)
        {
            return;
        }

        var cost = ClaudeCostCalculator.TryComputeUsd(usageLine.ModelSlug, usageLine.Usage, usageLine.Timestamp);

        // Sticky-null: once a model's cost has gone unknown for this file, it stays unknown until
        // the self-heal path in ProcessFile fully re-derives it from scratch.
        if (costs.TryGetValue(usageLine.ModelSlug, out var existing) && existing is null)
        {
            return;
        }

        costs[usageLine.ModelSlug] = cost is null ? null : (existing ?? 0m) + cost.Value;
    }
}
