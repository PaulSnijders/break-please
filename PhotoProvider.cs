using System.Text.Json;

namespace BreakPlease;

public record Photo(Image Image, string Caption) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Downloads the Bing "photo of the day" images (last ~16 days per market, no API key needed)
/// into a local cache, and picks a random one for each break, avoiding recently shown ones.
/// </summary>
class PhotoProvider
{
    const int MaxCached = 150;
    // Markets often have different photos; first market wins for the caption of shared ones.
    static readonly string[] Markets = ["nl-NL", "en-US", "en-GB", "en-CA", "en-AU", "en-IN", "de-DE", "fr-FR"];
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    readonly Random random = new();

    static string Folder => Path.Combine(Settings.Dir, "photos");
    static string HistoryFile => Path.Combine(Folder, "shown.txt");

    /// <summary>Returns false when nothing could be fetched (e.g. no network yet after waking up).</summary>
    public async Task<bool> RefreshAsync()
    {
        int added = 0, failed = 0, requests = 0;
        Directory.CreateDirectory(Folder);
        foreach (var market in Markets)
        foreach (var idx in new[] { 0, 7 })
        {
            requests++;
            try
            {
                var json = await Http.GetStringAsync($"https://www.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n=8&mkt={market}");
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
            catch (Exception ex)
            {
                if (failed++ == 0) Log.Write($"Foto's ophalen mislukt ({market}): {ex.Message}");
            }
        }
        if (added > 0) Log.Write($"{added} nieuwe foto's gedownload");
        try { TrimCache(); } catch { }
        return failed < requests;
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

            // Skip the most recently shown half, so repeats are rare and new photos come up soon.
            var history = File.Exists(HistoryFile) ? File.ReadAllLines(HistoryFile).ToList() : [];
            var recent = history.TakeLast(files.Length / 2).ToHashSet();
            var candidates = files.Where(f => !recent.Contains(Path.GetFileName(f))).ToArray();
            if (candidates.Length == 0) candidates = files;
            var file = candidates[random.Next(candidates.Length)];
            history.Add(Path.GetFileName(file));
            File.WriteAllLines(HistoryFile, history.TakeLast(MaxCached));

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
