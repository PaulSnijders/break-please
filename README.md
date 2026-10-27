# Break Please

Simple break reminder for Windows (replaces Stretchly). Runs in the system tray.

- Break every N minutes, with a small warning beforehand, then a blurred screen with a Bing photo of the day and a progress bar.
- No mouse/keyboard input for 2 minutes (configurable) counts as a break: the timer resets.
- No breaks while the microphone or webcam is in use (Teams, Meet, Zoom, ...). A break that falls due during a call comes ~1 minute after it ends.
- Settings: double-click the tray icon. Esc or "Overslaan" skips a break.

Data lives in `%APPDATA%\BreakPlease` (`settings.json`, `logs\yyyy-MM-dd.log`, `photos\`).
Set `BREAKPLEASE_DIR` to use a different folder (for testing).

## Install

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "$env:LOCALAPPDATA\Programs\BreakPlease"
& "$env:LOCALAPPDATA\Programs\BreakPlease\BreakPlease.exe"
```

The Release build registers itself in the Windows startup (HKCU Run) on launch, unless disabled in settings.
