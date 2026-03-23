# Buffero

Buffero is a Windows-only replay buffer desktop app. It runs as a small tray utility, watches for configured games, keeps a rolling video buffer, and saves the last part of gameplay to MP4 when you press the save hotkey.

The current design document source of truth is [Buffero Design Document.txt](./Buffero%20Design%20Document.txt).

## Current state

- WPF desktop app targeting `net9.0-windows`
- Single-instance tray app with settings/status window
- Custom executable, window, and tray icon
- Start with Windows support and background launch via `--background`
- Global replay buffer enable / disable control from the settings window and tray menu
- Auto-start / auto-stop buffering when an allowed game window stays eligible
- Foreground window hook for faster auto-detection, with polling fallback
- Startup scan for new games from Steam libraries, Epic manifests, and other common install folders
- Segment-based rolling replay buffer
- MP4 export to `%UserProfile%\Videos\Buffero Videos`
- Replay export is blocked when the save drive is critically low on free space
- Default save hotkey is `Alt+P`
- `Right Alt` / `AltGr` save support through the alternate registration path
- Selectable window or full-display capture mode
- Selectable native, max `1080p`, or max `720p` capture resolution
- In-game `Recording saved` overlay on the recorded game window
- Tray notification fallback when a replay is saved
- Replay-saved overlay and tray notifications can be disabled in settings
- Diagnostics show save-drive and temp-drive free space
- JSON settings under `%LocalAppData%\Buffero\settings.json`
- Log files under `%LocalAppData%\Buffero\logs`
- xUnit coverage for the replay-domain/core logic

## Current implementation notes

- Capture currently uses `ffmpeg` with `gdigrab`
- Buffero can capture either the matched game window region or the full desktop
- Replay export currently re-encodes buffered segments into the final MP4
- Auto-start uses a ~2.5 second debounce and requires a valid game window only when window capture mode is selected
- Foreground change detection uses `SetWinEventHook`, with the periodic scan retained as a fallback
- Replay export checks available free space on the configured save drive before queueing `ffmpeg`
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

## Releases

- Push to `main` updates a rolling prerelease named `Buffero Nightly` with a downloadable `Buffero-win-x64-nightly.zip` asset in GitHub Releases.
- Push a version tag like `v1.0.0` to create a versioned GitHub Release with a matching `Buffero-win-x64-v1.0.0.zip` asset.
- The release workflow is defined in [.github/workflows/publish-release-build.yml](./.github/workflows/publish-release-build.yml).

## Main paths

- Settings: `%LocalAppData%\Buffero\settings.json`
- Logs: `%LocalAppData%\Buffero\logs`
- Temp capture data: `%LocalAppData%\Buffero\temp`
- Saved clips: `%UserProfile%\Videos\Buffero Videos`
