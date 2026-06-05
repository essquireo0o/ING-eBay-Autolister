using System.Text.Json;

namespace ING_eBay_AutoLister.Services;

public class AnalyticsData
{
    public int TotalPageLoads { get; set; }
    public List<string> UniqueIps { get; set; } = [];
    public int AiAnalyses { get; set; }
    public int BulkImports { get; set; }
    public int ListingsPublished { get; set; }
    public int DraftsSaved { get; set; }
    public DateTimeOffset? FirstSeen { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public List<DailyStats> Daily { get; set; } = [];
}

public class DailyStats
{
    public string Date { get; set; } = "";
    public int PageLoads { get; set; }
    public int UniqueIps { get; set; }
    public int AiAnalyses { get; set; }
    public int BulkImports { get; set; }
    public int ListingsPublished { get; set; }
}

public class AnalyticsStore
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private AnalyticsData _data;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public AnalyticsStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "analytics.json");
        _data = Load();
    }

    public void RecordPageLoad(string ip)
    {
        lock (_sync)
        {
            _data.TotalPageLoads++;
            _data.LastSeen = DateTimeOffset.UtcNow;
            _data.FirstSeen ??= _data.LastSeen;
            if (!_data.UniqueIps.Contains(ip))
                _data.UniqueIps.Add(ip);
            var day = GetOrCreateToday();
            day.PageLoads++;
            day.UniqueIps = _data.UniqueIps.Count;
            Persist();
        }
    }

    public void RecordAiAnalysis()
    {
        lock (_sync) { _data.AiAnalyses++; GetOrCreateToday().AiAnalyses++; Persist(); }
    }

    public void RecordBulkImport()
    {
        lock (_sync) { _data.BulkImports++; GetOrCreateToday().BulkImports++; Persist(); }
    }

    public void RecordListingPublished()
    {
        lock (_sync) { _data.ListingsPublished++; GetOrCreateToday().ListingsPublished++; Persist(); }
    }

    public void RecordDraftSaved()
    {
        lock (_sync) { _data.DraftsSaved++; Persist(); }
    }

    public AnalyticsData GetSnapshot()
    {
        lock (_sync) { return _data; }
    }

    private DailyStats GetOrCreateToday()
    {
        var key = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var day = _data.Daily.Find(d => d.Date == key);
        if (day == null)
        {
            day = new DailyStats { Date = key };
            _data.Daily.Add(day);
            if (_data.Daily.Count > 30)
                _data.Daily.RemoveAt(0);
        }
        return day;
    }

    private void Persist() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _opts));

    private AnalyticsData Load()
    {
        if (!File.Exists(_filePath)) return new();
        try { return JsonSerializer.Deserialize<AnalyticsData>(File.ReadAllText(_filePath)) ?? new(); }
        catch { return new(); }
    }
}
