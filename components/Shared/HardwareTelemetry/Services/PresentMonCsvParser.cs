using System.Globalization;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Explicit PresentMon --v1_metrics, default TimeInSeconds. Never interpret QPC or V2 milliseconds as seconds.
public static class PresentMonCsvParser
{
    public static IReadOnlyList<PresentedFrameRate> Parse(string csv)
    {
        if (csv.Length > 4 * 1024 * 1024) throw new InvalidDataException("Frame CSV exceeds the bounded capture size.");
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (header is null) throw new InvalidDataException("No PresentMon CSV header.");
        var names = Fields(header.TrimStart('\uFEFF'));
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new InvalidDataException("Duplicate frame CSV columns.");
        int Column(string name)
        {
            var index = names.IndexOf(name);
            if (index < 0) throw new InvalidDataException("Required V1 CSV column missing: " + name);
            return index;
        }
        var app = Column("Application");
        var pid = Column("ProcessID");
        var chain = Column("SwapChainAddress");
        var time = Column("TimeInSeconds");
        var dropped = Column("Dropped");
        var streams = new Dictionary<(uint Pid, ulong Chain), PresentedFrameRate>();
        var rowCount = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (++rowCount > 100_000) throw new InvalidDataException("Too many frame events.");
            var fields = Fields(line);
            if (fields.Count != names.Count || string.IsNullOrWhiteSpace(fields[app]) ||
                !uint.TryParse(fields[pid], NumberStyles.None, CultureInfo.InvariantCulture, out var process) || process == 0 ||
                !fields[chain].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !ulong.TryParse(fields[chain].AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var address) ||
                !double.TryParse(fields[time], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0 ||
                fields[dropped] is not ("0" or "1"))
                throw new InvalidDataException("Malformed frame identity, time, or dropped flag.");
            var key = (process, address);
            var drop = fields[dropped] == "1" ? 1 : 0;
            if (!streams.TryGetValue(key, out var previous))
            {
                if (streams.Count >= 4096) throw new InvalidDataException("Too many swap chains.");
                streams[key] = new(process, fields[app], address, 1, drop, seconds, seconds, null);
                continue;
            }
            if (seconds <= previous.LastPresentSeconds || fields[app] != previous.Application)
                throw new InvalidDataException("Non-increasing per-chain timestamps or changed process identity.");
            var count = previous.PresentCount + 1;
            var rate = (count - 1) / (seconds - previous.FirstPresentSeconds);
            if (!double.IsFinite(rate)) throw new InvalidDataException("Invalid event rate.");
            streams[key] = previous with { PresentCount = count, DroppedCount = previous.DroppedCount + drop,
                LastPresentSeconds = seconds, PresentsPerSecond = address == 0 ? null : rate };
        }
        return streams.Values.OrderBy(value => value.ProcessId).ThenBy(value => value.SwapChainAddress).ToList();
    }

    private static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character != '"') value.Append(character);
                else if (index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                else { quoted = false; closed = true; }
            }
            else if (character == ',') { fields.Add(value.ToString()); value.Clear(); closed = false; }
            else if (character == '"' && value.Length == 0 && !closed) quoted = true;
            else if (character == '"' || closed) throw new InvalidDataException("Malformed CSV quoting.");
            else value.Append(character);
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV field.");
        fields.Add(value.ToString());
        return fields;
    }
}
