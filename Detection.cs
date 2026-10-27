using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BreakPlease;

static class Idle
{
    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>Time since the last mouse or keyboard input.</summary>
    public static TimeSpan Get()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}

/// <summary>
/// Detects calls (Teams, Meet in the browser, Zoom, ...) by checking whether any app is
/// currently using the microphone or webcam, via the Windows privacy "in use" registry.
/// </summary>
static class MeetingDetector
{
    const string BaseKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    /// <summary>Name of the app using mic/webcam, or null.</summary>
    public static string? ActiveApp()
    {
        try
        {
            return Find("microphone") ?? Find("webcam");
        }
        catch
        {
            return null;
        }
    }

    static string? Find(string capability)
    {
        using var root = Registry.CurrentUser.OpenSubKey(BaseKey + capability);
        return root == null ? null : Scan(root);
    }

    static string? Scan(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub == null) continue;

            if (name == "NonPackaged")
            {
                var found = Scan(sub);
                if (found != null) return found;
                continue;
            }

            if (sub.GetValue("LastUsedTimeStart") is long start && start > 0 &&
                sub.GetValue("LastUsedTimeStop") is long stop && stop == 0)
            {
                // Non-packaged apps are stored as a path with '#' instead of '\'.
                return Path.GetFileName(name.Replace('#', '\\'));
            }
        }
        return null;
    }
}
