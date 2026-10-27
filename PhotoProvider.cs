using System.Text.Json;

namespace BreakPlease;

public record Photo(Image Image, string Caption) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Downloads the Bing "photo of the day" images (last ~16 days, no API key needed)
/// into a local cache, and picks a random one for each break.
/// </summary>
class PhotoProvider
{
    const int MaxCached = 150;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    readonly Random random = new();
    string? lastShown;

    static string Folder => Path.Combine(Settings.Dir, "photos");

    public async Task RefreshAsync()
    {
        int added = 0;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var idx in new[] { 0, 7 })
            {
                var json = await Http.GetStringAsync($"https://www.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n=8&mkt=nl-NL");
                using var doc = JsonDocument.Parse(json);
                foreach (var img in doc.RootElement.GetProperty("images").EnumerateArray())
                {
                    var hash = img.GetProperty("hsh").GetString();
                    var urlBase = img.GetProperty("urlbase").GetString();
                    if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(urlBase)) continue;

                    var file = Path.Combine(Folder, hash + ".jpg");
                    if (File.Exists(file)) continue;

                    var bytes = await Http.GetByteArrayAsync($"https://www.bing.com{urlBase}_UHD.jpg");
                    await File.WriteAllBytesAsync(file, bytes);
                    await File.WriteAllTextAsync(Path.ChangeExtension(file, ".txt"), img.GetProperty("copyright").GetString() ?? "");
                    added++;
                }
            }
            if (added > 0) Log.Write($"{added} nieuwe foto's gedownload");
            TrimCache();
        }
        catch (Exception ex)
        {
            Log.Write($"Foto's ophalen mislukt: {ex.Message}");
        }
    }

    static void TrimCache()
    {
        var old = new DirectoryInfo(Folder).GetFiles("*.jpg")
            .OrderByDescending(f => f.CreationTime)
            .Skip(MaxCached);
        foreach (var f in old)
        {
            f.Delete();
            File.Delete(Path.ChangeExtension(f.FullName, ".txt"));
        }
    }

    public Photo? PickRandom()
    {
        try
        {
            if (!Directory.Exists(Folder)) return null;
            var files = Directory.GetFiles(Folder, "*.jpg");
            if (files.Length == 0) return null;

            var candidates = files.Length > 1 ? files.Where(f => f != lastShown).ToArray() : files;
            var file = candidates[random.Next(candidates.Length)];
            lastShown = file;

            // Load via a copy so the file isn't locked.
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var loaded = Image.FromStream(stream);
            var captionFile = Path.ChangeExtension(file, ".txt");
            var caption = File.Exists(captionFile) ? File.ReadAllText(captionFile) : "";
            return new Photo(new Bitmap(loaded), caption);
        }
        catch (Exception ex)
        {
            Log.Write($"Foto laden mislukt: {ex.Message}");
            return null;
        }
    }
}
