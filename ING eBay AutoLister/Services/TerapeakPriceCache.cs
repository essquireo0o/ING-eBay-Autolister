using System.Text.Json;

namespace ING_eBay_AutoLister.Services;

public class TerapeakCacheEntry
{
    public decimal Average { get; set; }
    public decimal Median { get; set; }
    public decimal AvgShipping { get; set; }
    public decimal? SellThroughPercent { get; set; }
    public DateTime ScrapedAtUtc { get; set; }
}

// Terapeak is a real logged-in browser scrape, not an API — each call is ~5-40s, and hammering
// it risks getting the saved session logged out (see TerapeakService.ScrapeAsync). The one thing
// this app has plenty of is time, so every scrape's result is cached here and reused for any
// query that asks the same keyword again within the TTL window — whether that ask comes from a
// manual Opportunity Finder search or the Gem Radar background scanner. Persisted to disk so it
// compounds across restarts: the longer this runs, the more of the keyword space is already
// priced for free, and the fewer real scrapes any given search needs to spend.
public class TerapeakPriceCache
{
    private const int MaxEntries = 5000;

    private readonly string _filePath;
    private readonly object _sync = new();
    private Dictionary<string, TerapeakCacheEntry> _data;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public TerapeakPriceCache(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "terapeak-cache.json");
        _data = Load();
    }

    public TerapeakCacheEntry? TryGet(string query, TimeSpan maxAge)
    {
        var key = Normalize(query);
        lock (_sync)
        {
            if (_data.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.ScrapedAtUtc <= maxAge)
                return entry;
            return null;
        }
    }

    public void Set(string query, decimal average, decimal median, decimal avgShipping, decimal? sellThroughPercent)
    {
        var key = Normalize(query);
        lock (_sync)
        {
            _data[key] = new TerapeakCacheEntry
            {
                Average = average, Median = median, AvgShipping = avgShipping,
                SellThroughPercent = sellThroughPercent, ScrapedAtUtc = DateTime.UtcNow
            };
            if (_data.Count > MaxEntries)
            {
                var stale = _data.OrderBy(kv => kv.Value.ScrapedAtUtc).Take(_data.Count - MaxEntries).Select(kv => kv.Key).ToList();
                foreach (var staleKey in stale) _data.Remove(staleKey);
            }
            Persist();
        }
    }

    public int Count { get { lock (_sync) return _data.Count; } }

    private static string Normalize(string query) => query.Trim().ToLowerInvariant();

    private void Persist() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _opts));

    private Dictionary<string, TerapeakCacheEntry> Load()
    {
        if (!File.Exists(_filePath)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, TerapeakCacheEntry>>(File.ReadAllText(_filePath)) ?? new(); }
        catch { return new(); }
    }
}
