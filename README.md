# Buffero

Buffero is a Windows-only replay buffer desktop app. It runs as a small tray utility, watches for configured games, keeps a rolling video buffer, and saves the last part of gameplay to MP4 when you press the save hotkey.

The current design document source of truth is [Buffero Design Document.txt](C:/Users/samin/Desktop/Buffero/Buffero%20Design%20Document.txt).

## Current state

- WPF desktop app targeting `net9.0-windows`
- Single-instance tray app with settings/status window
- Start with Windows support and background launch via `--background`
- Auto-start / auto-stop buffering when an allowed game is the active foreground window
- Startup scan for new games from Steam libraries, Epic manifests, and other common install folders
- Segment-based rolling replay buffer
- MP4 export to `%UserProfile%\Videos\Buffero Videos`
- Default save hotkey is `Alt+P`
- `Right Alt` / `AltGr` save support through the alternate registration path
- In-game `Recording saved` overlay on the recorded game window
- Tray notification fallback when a replay is saved
- JSON settings under `%LocalAppData%\Buffero\settings.json`
- Log files under `%LocalAppData%\Buffero\logs`
- xUnit coverage for the replay-domain/core logic

## Current implementation notes

- Capture currently uses `ffmpeg` with `gdigrab`
- Buffero tries to capture the active game window region instead of the full desktop
- Replay export currently re-encodes buffered segments into the final MP4
- Game autodetection is still executable-list based; the startup scan expands that list automatically
- System audio is still disabled in this MVP

## Requirements

- Windows 10 or Windows 11
- .NET 9 SDK for building from source
- `ffmpeg.exe` available through WinGet or `PATH`, or set manually in Buffero settings

Buffero already probes common `ffmpeg` locations, including the WinGet link path:

```text
%LocalAppData%\Microsoft\WinGet\Links\ffmpeg.exe
```

## Build

```powershell
dotnet build Buffero.sln
```

## Run

From source:

```powershell
dotnet run --project .\Buffero.App\Buffero.App.csproj
```

Built executable:

```powershell
.\Buffero.App\bin\Debug\net9.0-windows\Buffero.App.exe
```

Background launch:

```powershell
.\Buffero.App\bin\Debug\net9.0-windows\Buffero.App.exe --background
```

## Test

```powershell
dotnet test .\Buffero.Tests\Buffero.Tests.csproj
```

## Main paths

- Settings: `%LocalAppData%\Buffero\settings.json`
- Logs: `%LocalAppData%\Buffero\logs`
- Temp capture data: `%LocalAppData%\Buffero\temp`
- Saved clips: `%UserProfile%\Videos\Buffero Videos`
