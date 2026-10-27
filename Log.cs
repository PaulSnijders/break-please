namespace BreakPlease;

/// <summary>One log file per day in %APPDATA%\BreakPlease\logs, kept for 30 days.</summary>
static class Log
{
    static readonly object Lock = new();

    public static string Folder => Path.Combine(Settings.Dir, "logs");

    public static void Write(string message)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }

    public static void Cleanup(int keepDays = 30)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, "*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-keepDays))
                    File.Delete(file);
        }
        catch { }
    }
}
