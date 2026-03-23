namespace Buffero.Core.Capture;

public sealed class SegmentCatalog
{
    private readonly List<SegmentInfo> _segments = [];
    private readonly int _segmentSeconds;

    public SegmentCatalog(int segmentSeconds)
    {
        _segmentSeconds = Math.Max(1, segmentSeconds);
    }

    public IReadOnlyList<SegmentInfo> Segments => _segments;

    public long TotalSizeBytes => _segments.Sum(segment => segment.SizeBytes);

    public void ReplaceSegments(IEnumerable<SegmentInfo> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments.OrderBy(segment => segment.Sequence));
    }

    public IReadOnlyList<SegmentInfo> GetReplaySnapshot(int bufferSeconds)
    {
        if (_segments.Count == 0)
        {
            return [];
        }

        var requiredCount = Math.Max(1, (int)Math.Ceiling(bufferSeconds / (double)_segmentSeconds));
        return _segments.TakeLast(requiredCount).ToArray();
    }

    public IReadOnlyList<SegmentInfo> GetOverflowSegments(int bufferSeconds, long maxBytes)
    {
        if (_segments.Count == 0)
        {
            return [];
        }

        var keepCount = Math.Max(1, (int)Math.Ceiling(bufferSeconds / (double)_segmentSeconds)) + 2;
        var protectedStartIndex = Math.Max(0, _segments.Count - keepCount);
        var overflow = new List<SegmentInfo>();

        if (protectedStartIndex > 0)
        {
            overflow.AddRange(_segments.Take(protectedStartIndex));
        }

        var remainingBytes = _segments
            .Skip(overflow.Count)
            .Sum(segment => segment.SizeBytes);

        if (remainingBytes <= maxBytes)
        {
            return overflow;
        }

        for (var index = overflow.Count; index < protectedStartIndex; index++)
        {
            remainingBytes -= _segments[index].SizeBytes;
            overflow.Add(_segments[index]);

            if (remainingBytes <= maxBytes)
            {
                break;
            }
        }

        return overflow
            .DistinctBy(segment => segment.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(segment => segment.Sequence)
            .ToArray();
    }
}
