using ING_eBay_AutoLister.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ING_eBay_AutoLister.Services;

public class DraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private string DraftsDir
    {
        get
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var dir = Path.Combine(desktop, "eBayListing");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public string EnsureFolder() => DraftsDir;

    public List<DraftSummary> ListDrafts()
    {
        return Directory.GetFiles(DraftsDir, "*.json")
            .Select(f =>
            {
                try
                {
                    using var stream = File.OpenRead(f);
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    var title   = root.TryGetProperty("title",   out var t) ? t.GetString() ?? "Untitled" : "Untitled";
                    var savedAt = root.TryGetProperty("savedAt", out var s) ? s.GetString() ?? "" : "";
                    return new DraftSummary(Path.GetFileName(f), title, savedAt);
                }
                catch { return null; }
            })
            .Where(d => d != null)
            .Cast<DraftSummary>()
            .OrderByDescending(d => d.SavedAt)
            .ToList();
    }

    public DraftFile? LoadDraft(string filename)
    {
        var path = SafePath(filename);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<DraftFile>(File.ReadAllText(path), JsonOptions);
    }

    public string SaveDraft(DraftFile draft)
    {
        draft.SavedAt = DateTimeOffset.UtcNow.ToString("O");

        var filename = !string.IsNullOrWhiteSpace(draft.Filename)
            ? Sanitize(draft.Filename)
            : $"{Slugify(draft.Title)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json";

        draft.Filename = filename;
        File.WriteAllText(SafePath(filename), JsonSerializer.Serialize(draft, JsonOptions));
        return filename;
    }

    public void DeleteDraft(string filename)
    {
        var path = SafePath(filename);
        if (File.Exists(path)) File.Delete(path);
    }

    private string SafePath(string filename) => Path.Combine(DraftsDir, Sanitize(filename));

    private static string Sanitize(string name) =>
        Regex.Replace(Path.GetFileName(name), @"[^a-zA-Z0-9_\-\.]", "_");

    private static string Slugify(string? input)
    {
        var s = Regex.Replace((input ?? "draft").ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        return s.Length > 40 ? s[..40] : (s.Length == 0 ? "draft" : s);
    }
}

public record DraftSummary(string Filename, string Title, string SavedAt);

public class DraftFile
{
    public string? Filename         { get; set; }
    public string  Title            { get; set; } = "";
    public string  SavedAt          { get; set; } = "";
    public PostListingRequest Data  { get; set; } = new();
    public string? ImageBase64      { get; set; }
    public string? MimeType         { get; set; }
    public string? VisualDescription { get; set; }
}
