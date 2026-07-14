using System.Text.Json;
using ING_eBay_AutoLister.Models;

namespace ING_eBay_AutoLister.Services;

public class GemRadarData
{
    public List<GemEntry> Gems { get; set; } = [];
    public DateTime? LastScanUtc { get; set; }
    public string? LastScanCategory { get; set; }
    public int TotalScans { get; set; }
}

// Persists the Gem Radar background scanner's finds, following the same JSON-file-on-disk
// pattern as AnalyticsStore — no database in this project.
public class GemRadarStore
{
    private const int MaxGems = 200;

    private readonly string _filePath;
    private readonly object _sync = new();
    private GemRadarData _data;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public GemRadarStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gem-radar.json");
        _data = Load();
    }

    // Merges one category's scan results into the store: listings found again get their (possibly
    // moved) price/score refreshed, listings from this category that weren't found again this pass
    // are dropped (sold/ended/fell out of the top results), and anything past its own end date is
    // pruned regardless of category — keeps the feed live instead of accumulating stale auctions.
    public void RecordScan(string category, IEnumerable<OpportunityListItem> found)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var foundList = found.ToList();
            var foundUrls = foundList.Select(f => f.Url).ToHashSet();

            _data.Gems.RemoveAll(g => g.Category == category && !foundUrls.Contains(g.Item.Url));

            foreach (var item in foundList)
            {
                var existing = _data.Gems.FirstOrDefault(g => g.Item.Url == item.Url);
                if (existing is not null) existing.Item = item;
                else _data.Gems.Add(new GemEntry { Category = category, FoundAtUtc = now, Item = item });
            }

            _data.Gems.RemoveAll(g => g.Item.EndDate.HasValue && g.Item.EndDate.Value < now);
            if (_data.Gems.Count > MaxGems)
                _data.Gems = _data.Gems.OrderByDescending(g => g.Item.OpportunityScore ?? 0).Take(MaxGems).ToList();

            _data.LastScanUtc = now;
            _data.LastScanCategory = category;
            _data.TotalScans++;
            Persist();
        }
    }

    public GemRadarData GetSnapshot()
    {
        lock (_sync) return _data;
    }

    private void Persist() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _opts));

    private GemRadarData Load()
    {
        if (!File.Exists(_filePath)) return new();
        try { return JsonSerializer.Deserialize<GemRadarData>(File.ReadAllText(_filePath)) ?? new(); }
        catch { return new(); }
    }
}
