using System.ComponentModel;
using System.Runtime.CompilerServices;
using Monitoring.Contracts;

namespace MenuBar.Models;

public sealed class StatsMetricItem : INotifyPropertyChanged
{
    private string _value = "--";
    private string _tooltip = string.Empty;

    public StatsMetricItem(StatsMetricDefinition definition, string? accent = null)
    {
        Id = definition.Id;
        Title = definition.Title;
        Accent = MetricColors.Normalize(accent, definition.Accent);
    }

    public string Id { get; }
    public string Title { get; }
    public string Accent { get; }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        set => SetField(ref _tooltip, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
