using System.IO;
using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using HardwareTelemetry.Services;
using Monitoring.Contracts;

namespace MenuBar.Services;

public sealed class MetricsPublisher : IDisposable
{
    private readonly IHardwareMetricsReader _reader;
    private readonly StatsSettingsStore _settingsStore;
    private readonly LiveMetricsHub _liveMetrics;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedFile _legacyMapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly MemoryMappedViewAccessor _legacyView;
    private readonly Timer _timer;
    private readonly PublisherPerformanceWriter _performance;
    private long _configurationCount;
    private readonly object _lifecycleGate = new();
    private bool _disposed;
    private bool _captureInProgress;
    private bool _wakePending;
    private uint _enabledMask;
    private uint[] _orderedIds = [];
    private byte[] _styles = [];
    private uint[] _colors = [];
    private MetricsCollectorDemand? _appliedDemand;
    private MetricsCollectorDemand _configuredDemand;
    private StatsAlertRule[] _enabledAlerts = [];
    private List<StatsMetricPreference> _preferences;
    private StatsApplicationSettings _settings;
    private DateTime _settingsWriteTimeUtc;
    private long _publishSequence;
    private readonly Dictionary<string, AlertRuntimeState> _alertStates = new(StringComparer.Ordinal);

    public MetricsPublisher(LiveMetricsHub? liveMetrics = null)
        : this(new HardwareMetricsReader(), new StatsSettingsStore(), liveMetrics,
            SharedMetricsLayout.MapName, LegacySharedMetricsLayout.MapName, PublisherDiagnostics.MapName)
    {
    }

    internal MetricsPublisher(IHardwareMetricsReader reader, StatsSettingsStore settingsStore,
        LiveMetricsHub? liveMetrics, string mapName, string legacyMapName, string performanceMapName)
    {
        _reader = reader;
        _settingsStore = settingsStore;
        _liveMetrics = liveMetrics ?? new LiveMetricsHub();
        _settings = _settingsStore.LoadSettings();
        _preferences = _settings.Metrics.ToList();
        PrepareConfiguration();
        _settingsWriteTimeUtc = ReadSettingsWriteTime();
        _mapping = MemoryMappedFile.CreateOrOpen(
            mapName,
            SharedMetricsLayout.Capacity,
            MemoryMappedFileAccess.ReadWrite);
        try
        {
            _legacyMapping = MemoryMappedFile.CreateOrOpen(legacyMapName, SharedMetricsLayout.Capacity, MemoryMappedFileAccess.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, SharedMetricsLayout.Capacity, MemoryMappedFileAccess.Write);
            _legacyView = _legacyMapping.CreateViewAccessor(0, SharedMetricsLayout.Capacity, MemoryMappedFileAccess.Write);
            _performance = new PublisherPerformanceWriter(performanceMapName);
            // Never schedule a callback before the constructor has assigned the timer.
            _timer = new Timer(Capture, null, Timeout.Infinite, Timeout.Infinite);
        }
        catch
        {
            _view?.Dispose();
            _legacyView?.Dispose();
            _legacyMapping?.Dispose();
            _performance?.Dispose();
            _mapping.Dispose();
            (reader as IDisposable)?.Dispose();
            throw;
        }
        _liveMetrics.InterestsChanged += OnInterestsChanged;
        _timer.Change(0, Timeout.Infinite);
    }

    public event Action<StatsAlertNotification>? AlertRaised;
    public LiveMetricsHub LiveMetrics => _liveMetrics;

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _liveMetrics.InterestsChanged -= OnInterestsChanged;
        // Drain queued/running callbacks before closing their reader or shared views.
        _timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _view.Dispose();
        _legacyView.Dispose();
        _performance.Dispose();
        _mapping.Dispose();
        _legacyMapping.Dispose();
        if (_reader is IDisposable disposableReader)
        {
            disposableReader.Dispose();
        }
    }

    private void Capture(object? state)
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _captureInProgress) return;
            _captureInProgress = true;
            _wakePending = false;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            ReloadSettingsIfChanged();
            var demand = _configuredDemand.Combine(_liveMetrics.GetCollectorInterests());
            if (_appliedDemand != demand)
            {
                // Only the capture worker mutates reader flags, never a popup/UI thread.
                demand.Apply(_reader);
                _appliedDemand = demand;
            }
            var metrics = _reader.Capture();
            var maximumSamples = Math.Max(2, _settings.HistorySeconds * 1000 / _settings.RefreshIntervalMilliseconds);
            _liveMetrics.Publish(metrics, maximumSamples);
            EvaluateAlerts(metrics);
            var completedSequence = Interlocked.Add(ref _publishSequence, 2);
            WriteSnapshot(_view, SharedMetricsLayout.Version, SensorMetricSelector.PreferredTemperatureCelsius(metrics), metrics, completedSequence);
            WriteSnapshot(_legacyView, LegacySharedMetricsLayout.Version, metrics.PowerWatts, metrics, completedSequence);
            _performance.Record(demand, _configurationCount, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            lock (_lifecycleGate)
            {
                _captureInProgress = false;
                if (!_disposed)
                {
                    // One follow-up for a burst of interest changes, not overlapping polling jobs.
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var due = _wakePending ? 0 : Math.Max(0, _settings.RefreshIntervalMilliseconds - elapsed);
                    _timer.Change(TimeSpan.FromMilliseconds(due), Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void WriteSnapshot(
        MemoryMappedViewAccessor view,
        uint version,
        double? fourthMetric,
        HardwareTelemetry.Models.HardwareMetrics metrics,
        long completedSequence)
    {
            // Odd sequence must precede every payload write; even sequence is the commit.
            view.Write(8, completedSequence - 1);
            Thread.MemoryBarrier();
            view.Write(0, SharedMetricsLayout.Magic);
            view.Write(4, version);
            view.Write(SharedMetricsLayout.CpuOffset, metrics.CpuPercent ?? double.NaN);
            view.Write(SharedMetricsLayout.GpuOffset, metrics.GpuPercent ?? double.NaN);
            view.Write(SharedMetricsLayout.MemoryOffset, metrics.MemoryPercent ?? double.NaN);
            view.Write(SharedMetricsLayout.SensorOffset, fourthMetric ?? double.NaN);
            view.Write(SharedMetricsLayout.CpuTemperatureOffset, metrics.CpuTemperatureCelsius ?? double.NaN);
            view.Write(SharedMetricsLayout.CpuFrequencyOffset, metrics.CpuFrequencyMhz ?? double.NaN);
            view.Write(SharedMetricsLayout.GpuTemperatureOffset, metrics.GpuTemperatureCelsius ?? double.NaN);
            view.Write(SharedMetricsLayout.GpuPowerOffset, metrics.GpuPowerWatts ?? double.NaN);
            view.Write(SharedMetricsLayout.GpuMemoryUsedOffset, metrics.GpuMemoryUsedMb ?? double.NaN);
            view.Write(SharedMetricsLayout.GpuMemoryTotalOffset, metrics.GpuMemoryTotalMb ?? double.NaN);
            view.Write(SharedMetricsLayout.BatteryOffset, metrics.BatteryPercent ?? double.NaN);
            view.Write(SharedMetricsLayout.DiskOffset, metrics.DiskUsedPercent ?? double.NaN);
            view.Write(SharedMetricsLayout.NetworkDownloadOffset, metrics.NetworkDownloadBytesPerSecond ?? double.NaN);
            view.Write(SharedMetricsLayout.NetworkUploadOffset, metrics.NetworkUploadBytesPerSecond ?? double.NaN);
            view.Write(SharedMetricsLayout.BluetoothOffset, metrics.BluetoothConnectedDeviceCount ?? double.NaN);

            view.Write(SharedMetricsLayout.EnabledMaskOffset, _enabledMask);
            view.Write(SharedMetricsLayout.OrderCountOffset, (uint)_orderedIds.Length);
            for (var index = 0; index < StatsMetricCatalog.All.Count; index++)
            {
                view.Write(SharedMetricsLayout.OrderOffset + index * 4, index < _orderedIds.Length ? _orderedIds[index] : uint.MaxValue);
                view.Write(SharedMetricsLayout.StyleOffset + index, _styles[index]);
                view.Write(SharedMetricsLayout.ColorOffset + index * sizeof(uint), _colors[index]);
            }
            Thread.MemoryBarrier();
            view.Write(8, completedSequence);
            // Pagefile-backed IPC has no file to persist. Flush is not a seqlock fence.
    }

    private void ReloadSettingsIfChanged()
    {
        var writeTime = ReadSettingsWriteTime();
        if (writeTime == _settingsWriteTimeUtc)
        {
            return;
        }

        _settingsWriteTimeUtc = writeTime;
        _settings = _settingsStore.LoadSettings();
        _preferences = _settings.Metrics.ToList();
        _alertStates.Clear();
        PrepareConfiguration();
    }

    private DateTime ReadSettingsWriteTime()
    {
        try
        {
            return File.Exists(_settingsStore.SettingsPath)
                ? File.GetLastWriteTimeUtc(_settingsStore.SettingsPath)
                : DateTime.MinValue;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private void PrepareConfiguration()
    {
        ++_configurationCount;
        _configuredDemand = MetricsCollectorDemand.Resolve(_settings, default);
        _enabledAlerts = _settings.Alerts?.Where(item => item.IsEnabled).ToArray() ?? [];
        var positions = StatsMetricCatalog.All.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        var ordered = new List<uint>();
        _enabledMask = 0;
        foreach (var preference in _preferences)
        {
            if (!positions.TryGetValue(preference.Id, out var index)) continue;
            ordered.Add((uint)index);
            if (preference.IsEnabled) _enabledMask |= 1U << index;
        }
        _orderedIds = ordered.Take(StatsMetricCatalog.All.Count).ToArray();
        _styles = new byte[StatsMetricCatalog.All.Count];
        _colors = new uint[StatsMetricCatalog.All.Count];
        for (var index = 0; index < StatsMetricCatalog.All.Count; ++index)
        {
            var definition = StatsMetricCatalog.All[index];
            var preference = _preferences.FirstOrDefault(item => item.Id == definition.Id);
            _styles[index] = MetricDisplayStyles.ToProtocolValue(preference?.DisplayStyle ?? MetricDisplayStyles.Stacked);
            _colors[index] = MetricColors.ToProtocolArgb(MetricColors.Normalize(preference?.Accent, definition.Accent));
        }
    }

    private void OnInterestsChanged()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            if (_captureInProgress) _wakePending = true;
            else _timer.Change(0, Timeout.Infinite);
        }
    }

    private void EvaluateAlerts(HardwareTelemetry.Models.HardwareMetrics metrics)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var rule in _enabledAlerts)
        {
            var value = rule.MetricId switch
            {
                MetricIds.Cpu => metrics.CpuPercent,
                MetricIds.Gpu => metrics.GpuPercent,
                MetricIds.Ram => metrics.MemoryPercent,
                MetricIds.CpuTemperature => metrics.CpuTemperatureCelsius,
                MetricIds.GpuTemperature => metrics.GpuTemperatureCelsius,
                MetricIds.Battery => metrics.BatteryPercent,
                _ => null
            };
            if (value is null || !double.IsFinite(value.Value))
            {
                _alertStates.Remove(rule.MetricId);
                continue;
            }

            var breached = rule.Comparison == "below" ? value.Value <= rule.Threshold : value.Value >= rule.Threshold;
            if (!breached)
            {
                _alertStates.Remove(rule.MetricId);
                continue;
            }

            if (!_alertStates.TryGetValue(rule.MetricId, out var state))
            {
                state = new AlertRuntimeState(now, DateTimeOffset.MinValue);
                _alertStates[rule.MetricId] = state;
            }

            if (now - state.BreachStarted < TimeSpan.FromSeconds(rule.SustainSeconds) ||
                now - state.LastRaised < TimeSpan.FromMinutes(15))
            {
                continue;
            }

            _alertStates[rule.MetricId] = state with { LastRaised = now };
            var definition = StatsMetricCatalog.Find(rule.MetricId);
            var unit = rule.MetricId is MetricIds.CpuTemperature or MetricIds.GpuTemperature ? "°C" : "%";
            AlertRaised?.Invoke(new StatsAlertNotification(
                rule.MetricId,
                definition?.Description ?? rule.MetricId,
                value.Value,
                $"当前 {value.Value:0}{unit}，已{(rule.Comparison == "below" ? "低于" : "超过")}阈值 {rule.Threshold:0}{unit}"));
        }
    }

    private sealed record AlertRuntimeState(DateTimeOffset BreachStarted, DateTimeOffset LastRaised);
}

public sealed record StatsAlertNotification(string MetricId, string Title, double Value, string Message);
