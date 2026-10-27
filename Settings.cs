using System.Text.Json;
using Microsoft.Win32;

namespace BreakPlease;

public class Settings
{
    public int IntervalMinutes { get; set; } = 20;
    public int BreakSeconds { get; set; } = 20;
    public int WarningSeconds { get; set; } = 10;
    public int IdleResetSeconds { get; set; } = 120;
    public bool DetectMeetings { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    // BREAKPLEASE_DIR overrides the data folder, handy for testing.
    public static string Dir => Environment.GetEnvironmentVariable("BREAKPLEASE_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreakPlease");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            Log.Write($"Instellingen laden mislukt: {ex.Message}");
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        ApplyAutostart();
    }

    public void ApplyAutostart()
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key == null) return;
        if (StartWithWindows)
            key.SetValue("BreakPlease", $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue("BreakPlease", throwOnMissingValue: false);
    }
}
