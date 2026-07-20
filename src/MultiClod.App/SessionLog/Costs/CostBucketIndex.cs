namespace MultiClod.App.SessionLog.Costs;

/// <summary>
/// The global, incrementally-maintained, equally-spaced X-axis index shared across every series
/// (main + all agents). Every constructed CostTimelinePoint (cost-bearing or hold-flat) resolves
/// its column through this same index - a hold-flat line's second can fall between two
/// already-known cost buckets from a different, faster-writing file, which is the "occasional
/// out-of-order insert" case this handles. Points never cache a column permanently; every redraw
/// re-resolves BucketKey -> column fresh, so a rare renumber needs no invalidation logic elsewhere.
/// </summary>
internal sealed class CostBucketIndex
{
    private readonly object gate = new();
    private readonly List<long> sortedKeys = new();
    private readonly Dictionary<long, int> columnByKey = new();

    public int ColumnCount
    {
        get
        {
            lock (this.gate)
            {
                return this.sortedKeys.Count;
            }
        }
    }

    // O(log n) common-case append (concurrent tailers' timestamps mostly increase); O(n) renumber
    // only on the rare out-of-order insert caused by clock skew between concurrent files.
    public int GetOrAddColumn(long bucketKey)
    {
        lock (this.gate)
        {
            if (this.columnByKey.TryGetValue(bucketKey, out var existing))
            {
                return existing;
            }

            var insertAt = this.sortedKeys.BinarySearch(bucketKey);
            if (insertAt < 0)
            {
                insertAt = ~insertAt;
            }

            this.sortedKeys.Insert(insertAt, bucketKey);
            for (var i = insertAt; i < this.sortedKeys.Count; i++)
            {
                this.columnByKey[this.sortedKeys[i]] = i;
            }

            return this.columnByKey[bucketKey];
        }
    }

    public int? TryGetColumn(long bucketKey)
    {
        lock (this.gate)
        {
            return this.columnByKey.TryGetValue(bucketKey, out var column) ? column : null;
        }
    }
}
