namespace BreakPlease;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "BreakPlease.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
