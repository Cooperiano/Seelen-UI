using System.IO;
using System.Text.Json;
using MenuBar.Models;
using Monitoring.Contracts;

namespace MenuBar.Services;

public sealed class StatsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _legacyPath;

    public StatsSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Stats");
        _path = Path.Combine(directory, "stats-settings.json");
        _legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MenuBar",
            "stats-settings.json");
    }

    internal StatsSettingsStore(string path, string legacyPath)
    {
        _path = path;
        _legacyPath = legacyPath;
    }

    public string SettingsPath => _path;

    public List<StatsMetricPreference> Load() => LoadSettings().Metrics.ToList();

    public StatsApplicationSettings LoadSettings()
    {
        try
        {
            var sourcePath = File.Exists(_path) ? _path : _legacyPath;
            if (File.Exists(sourcePath))
            {
                var json = File.ReadAllText(sourcePath);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var legacy = JsonSerializer.Deserialize<List<StatsMetricPreference>>(json);
                    if (legacy is not null)
                    {
                        return Normalize(new StatsApplicationSettings(Normalize(legacy)));
                    }
                }
                else
                {
                    var loaded = JsonSerializer.Deserialize<StatsApplicationSettings>(json);
                    if (loaded is not null)
                    {
                        return Normalize(loaded);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file should never prevent the bar from starting.
        }

        return Normalize(new StatsApplicationSettings(StatsMetricCatalog.Defaults()));
    }

    public void Save(IEnumerable<StatsMetricPreference> preferences)
    {
        var current = LoadSettings();
        SaveSettings(current with { Metrics = Normalize(preferences) });
    }

    public void SaveSettings(StatsApplicationSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the live configuration even if persistence is temporarily unavailable.
        }
    }

    private static StatsApplicationSettings Normalize(StatsApplicationSettings settings) => settings with
    {
        Metrics = Normalize(settings.Metrics),
        RefreshIntervalMilliseconds = Math.Clamp(settings.RefreshIntervalMilliseconds, 500, 5000),
        HistorySeconds = Math.Clamp(settings.HistorySeconds, 30, 300),
        Alerts = NormalizeAlerts(settings.Alerts)
    };

    private static IReadOnlyList<StatsAlertRule> NormalizeAlerts(IReadOnlyList<StatsAlertRule>? alerts)
    {
        var source = alerts ?? [];
        return StatsAlertCatalog.Defaults.Select(defaultRule =>
        {
            var rule = source.FirstOrDefault(item => item.MetricId == defaultRule.MetricId) ?? defaultRule;
            return rule with
            {
                Threshold = Math.Clamp(rule.Threshold, 0, rule.MetricId == MetricIds.CpuFrequency ? 10000 : 200),
                Comparison = rule.Comparison == "below" ? "below" : "above",
                SustainSeconds = Math.Clamp(rule.SustainSeconds, 0, 300)
            };
        }).ToList();
    }

    private static List<StatsMetricPreference> Normalize(IEnumerable<StatsMetricPreference> preferences)
    {
        var result = new List<StatsMetricPreference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preference in preferences)
        {
            var definition = StatsMetricCatalog.Find(preference.Id);
            if (definition is null || !seen.Add(preference.Id))
            {
                continue;
            }

            result.Add(new StatsMetricPreference(
                preference.Id,
                preference.IsEnabled,
                MetricDisplayStyles.IsValid(preference.DisplayStyle) ? preference.DisplayStyle : MetricDisplayStyles.Stacked,
                MetricColors.Normalize(preference.Accent, definition.Accent)));
        }

        foreach (var definition in StatsMetricCatalog.All)
        {
            if (seen.Add(definition.Id))
            {
                result.Add(new StatsMetricPreference(
                    definition.Id,
                    definition.DefaultEnabled,
                    MetricDisplayStyles.Stacked,
                    definition.Accent));
            }
        }

        return result;
    }
}
