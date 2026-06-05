namespace ING_eBay_AutoLister.Services;

public sealed class ActionLog
{
    private readonly object _sync = new();
    private readonly List<ActionLogEntry> _entries = [];

    public ActionLog()
    {
        Add("Info", "ING Listing Engine™ started", "Official product of ING Mining LLC — ready.");
    }

    public void Add(string level, string title, string detail)
    {
        lock (_sync)
        {
            _entries.Insert(0, new ActionLogEntry(DateTimeOffset.UtcNow, level, title, detail));
            if (_entries.Count > 100) _entries.RemoveRange(100, _entries.Count - 100);
        }
    }

    public IReadOnlyList<ActionLogEntry> Recent()
    {
        lock (_sync)
        {
            return _entries.ToList();
        }
    }
}

public sealed record ActionLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Title,
    string Detail);
