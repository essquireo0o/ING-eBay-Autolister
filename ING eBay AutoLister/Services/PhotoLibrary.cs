namespace ING_eBay_AutoLister.Services;

public sealed class PhotoLibrary(IWebHostEnvironment env)
{
    private static readonly string[] DefaultFolders = ["S19_95TH", "S19_110TH", "S19j_Pro", "L7"];
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    private string RootPath => Path.Combine(env.ContentRootPath, "photos");

    public IReadOnlyList<PhotoFolderSummary> GetDefaultFolders()
    {
        Directory.CreateDirectory(RootPath);

        return DefaultFolders.Select(folder =>
        {
            var path = Path.Combine(RootPath, folder);
            Directory.CreateDirectory(path);

            var count = Directory.EnumerateFiles(path)
                .Count(file => ImageExtensions.Contains(Path.GetExtension(file)));

            return new PhotoFolderSummary(folder, path, count);
        }).ToList();
    }
}

public sealed record PhotoFolderSummary(
    string ModelKey,
    string Path,
    int ImageCount);
