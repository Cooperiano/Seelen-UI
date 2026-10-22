using System.IO;
using System.IO.MemoryMappedFiles;
using Monitoring.Contracts;

namespace MenuBar.Services;

internal sealed class MetricAnchorReader : IDisposable
{
    private readonly int _metricIndex;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;

    public MetricAnchorReader(string metricId)
    {
        _metricIndex = StatsMetricCatalog.All
            .Select((metric, index) => (metric, index))
            .Where(item => item.metric.Id == metricId)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
    }

    public bool TryRead(out ScreenRect bounds)
    {
        bounds = default;
        if (_metricIndex < 0 || _metricIndex >= StatsMetricCatalog.All.Count || !EnsureOpen())
        {
            return false;
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var firstSequence = _view!.ReadInt64(MetricAnchorLayout.SequenceOffset);
                if (firstSequence <= 0 || (firstSequence & 1) != 0)
                {
                    Thread.SpinWait(32);
                    continue;
                }

                if (_view.ReadUInt32(0) != MetricAnchorLayout.Magic ||
                    _view.ReadUInt32(4) != MetricAnchorLayout.Version ||
                    (_view.ReadUInt32(MetricAnchorLayout.VisibleMaskOffset) & (1U << _metricIndex)) == 0)
                {
                    return false;
                }

                var offset = MetricAnchorLayout.RectanglesOffset + _metricIndex * MetricAnchorLayout.RectangleSize;
                var candidate = new ScreenRect(
                    _view.ReadInt32(offset),
                    _view.ReadInt32(offset + 4),
                    _view.ReadInt32(offset + 8),
                    _view.ReadInt32(offset + 12));
                var secondSequence = _view.ReadInt64(MetricAnchorLayout.SequenceOffset);
                if (firstSequence == secondSequence && (secondSequence & 1) == 0 && candidate.IsValid)
                {
                    bounds = candidate;
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            CloseMapping();
        }

        return false;
    }

    public void Dispose() => CloseMapping();

    private bool EnsureOpen()
    {
        if (_view is not null)
        {
            return true;
        }

        try
        {
            _mapping = MemoryMappedFile.OpenExisting(
                MetricAnchorLayout.MapName,
                MemoryMappedFileRights.Read);
            _view = _mapping.CreateViewAccessor(
                0,
                MetricAnchorLayout.Capacity,
                MemoryMappedFileAccess.Read);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            CloseMapping();
            return false;
        }
    }

    private void CloseMapping()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }
}

internal readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public bool IsValid => Width > 0 && Height > 0;
}
