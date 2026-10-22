namespace HardwareTelemetry.Services;

// Windows EMI RAPL counters are picowatt-hours; WMI time is milliseconds, native EMI time is 100 ns.
// Keep a baseline, rather than presenting EMI's arbitrary zero-point as lifetime energy.
internal sealed class EnergyMeterSession
{
    private readonly Dictionary<string, (ulong Energy, ulong Time)> _baselines = new();
    private readonly Dictionary<string, (ulong Energy, ulong Time)> _latest = new();
    private readonly double _ticksPerSecond;
    private readonly double _unitsPerMilliWattHour;

    public EnergyMeterSession(double ticksPerSecond = 1000, double unitsPerMilliWattHour = 1_000_000_000)
    {
        if (!double.IsFinite(ticksPerSecond) || ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        _ticksPerSecond = ticksPerSecond;
        if (!double.IsFinite(unitsPerMilliWattHour) || unitsPerMilliWattHour <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMilliWattHour));
        _unitsPerMilliWattHour = unitsPerMilliWattHour;
    }

    public (double MilliWattHours, double AverageWatts)? Observe(string name, ulong energy, ulong time)
    {
        if (!_latest.TryGetValue(name, out var previous) || energy < previous.Energy || time < previous.Time)
        {
            _baselines[name] = (energy, time);
            _latest[name] = (energy, time);
            return null;
        }

        // Ignore inconsistent provider samples without destroying the session baseline.
        if (time == previous.Time && energy != previous.Energy) return null;
        _latest[name] = (energy, time);
        var baseline = _baselines[name];
        if (time <= baseline.Time) return null;
        var consumed = energy - baseline.Energy;
        return (consumed / _unitsPerMilliWattHour, consumed / _unitsPerMilliWattHour * 3.6d * _ticksPerSecond / (time - baseline.Time));
    }
}
