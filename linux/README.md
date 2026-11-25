# Hesper for Linux (preview)

The Windows app's code and pages (`win/Hesper`), with a Linux host. Same Python engine, same models
(faster-whisper, llama.cpp, sherpa-onnx), same features as on Windows except the Windows-only ones
(see below). One AppImage, for x86-64 (arm64 can be built the same way).

## How it's made

- **Shared with Windows** (compiled into both apps, listed in `Hesper/Hesper.Linux.csproj`):
  `AppModel.cs` (everything the pages ask), `Meetings.cs`, `Settings.cs`, `Catalog.cs`, `Setup.cs`,
  `EngineRunner.cs`, `Paths.cs`, `I18n.cs`, `Common.cs`, `CalendarEvent.cs`, and the pages
  (`win/Hesper/Ui`). Each of these files says so at its top. What differs between the two systems
  goes through `Platform` (`win/Hesper/Platform.cs` / `linux/Hesper/Services.cs`) or the platform's
  half of a partial class (`Share`, `Storage`, `Updater`, `Uninstall`).
- **Linux's own** (`Hesper/`), reaching the system's libraries through P/Invoke (`Native.cs`), no
  NuGet packages:
  - `WebWindow.cs`: a GTK 3 window with WebKitGTK 4.1 (4.0 works too); the pages are served from
    inside the program as `hesper-app://app/`; a small script gives them WebView2's
    `window.chrome.webview`, so `app.js` is the Windows one.
  - `TrayApp.cs`: an AppIndicator icon (GNOME with its AppIndicator extension, as Ubuntu has it; KDE,
    Cinnamon, Xfce…) whose menu opens the home window. **No tray on this desktop** (plain GNOME on
    Fedora…): the home window is the app; closing it hides it, opening Hesper again shows it.
  - `Recorder.cs`: the microphone and the computer's sound (the output's *monitor*) through
    PulseAudio's simple API, which PipeWire answers too (pipewire-pulse) → `mic.wav` / `system.wav`,
    16 kHz mono, as on Windows. Devices and calls (`CallMonitor`) through `pactl`.
  - `Watchers.cs`: calls noticed (a meeting app recording, from `pactl`), the global shortcut (X11
    sessions only: on Wayland, apps can't take one — Settings says how to bind `xdg-open
    hesper://toggle` in the desktop's keyboard settings), "nobody is using the computer" (Mutter's
    idle monitor or X11, logind's lock, `/sys/class/power_supply`).
  - `Services.cs`: `Platform` (clipboard, GTK's file chooser, the Trash, the file manager,
    `systemd-inhibit` while recording), the menu entry and `hesper://` links
    (`~/.local/share/applications/hesper.desktop`), "Open at login" (`~/.config/autostart`), the
    AppImage's self-update, uninstall, notifications (libnotify).
  - `Brand.cs`: the icons, drawn with cairo. `MainLoop.cs`: GTK's loop as the UI thread
    (`SynchronizationContext`, timers), D-Bus calls.
- Everything goes into `~/.local/share/Hesper` (engine, Python, models, settings); notes into
  `~/Documents/MeetingNotes` (the Documents folder in the user's language). No root, nothing
  outside the home folder. llama.cpp: the **Vulkan** build when there's an NVIDIA GPU (its driver
  brings Vulkan; no 600 MB of CUDA libraries), else the CPU one. faster-whisper uses CUDA through
  `uv sync --extra cuda` (cuBLAS and cuDNN wheels) on NVIDIA.

Not on Linux (Windows-only): classic Outlook's calendar (the **calendar link** — Google, Outlook,
Nextcloud, iCloud — is the calendar), OneDrive as a storage choice ("Another folder…" covers a
Nextcloud or Dropbox folder), recording Teams only, Outlook as the e-mail app.

## What the desktop needs

Present on Ubuntu, Debian, Fedora, Mint, openSUSE… desktops, mostly by default:

| Library | Debian / Ubuntu package | Fedora package | Without it |
|---|---|---|---|
| GTK 3, WebKitGTK 4.1 | `libwebkit2gtk-4.1-0` | `webkit2gtk4.1` | no window: Hesper says what to install |
| PulseAudio client (PipeWire too) | `libpulse0` | `pulseaudio-libs` | no recording |
| `pactl` | `pulseaudio-utils` | `pulseaudio-utils` | no device choice, no "Record this call?" |
| AppIndicator | `libayatana-appindicator3-1` | `libayatana-appindicator-gtk3` | a window instead of a tray icon |
| libnotify | `libnotify4` | `libnotify` | `notify-send`, or no notifications |

## Build

From the repository's root, with the .NET 8 SDK:

```bash
dotnet build linux/Hesper/Hesper.Linux.csproj          # compile (works on a Mac or Windows too)
linux/build_appimage.sh                                  # on Linux: dist/linux/Hesper-<version>-x86_64.AppImage
```

GitHub builds it on every push touching the app (`.github/workflows/linux.yml`, free for public
repositories): the AppImage, the Windows app's build (shared code), pictures of every page (light,
dark, French) taken under a virtual screen, a 3-second recording from a silent sound server, and the
engine's tests on Linux. All in the run's artifact. **Publishing**: Actions → *Linux app* → *Run
workflow* with a release tag (`v0.7.3`): the AppImage and `latest-linux.json` join that release; then
`scripts/linux_feed.sh v0.7.3` puts the feed in the repository, to commit and push (installed apps
update from it).

## Run and test

```bash
chmod +x Hesper-*.AppImage && ./Hesper-*.AppImage
```

The first launch registers Hesper in the applications menu (and as the `hesper://` handler), then the
welcome window sets everything up (4–6 GB of models).

Diagnostics (also for support): `--devices` (the microphones and outputs, and how loud each is over
3 s), `--record SECONDS [FOLDER] [--no-process]`, `--notify` (a test notification, and which server
shows them), `--capture DIR [LANG]` (pictures of every page), `--version`. `HESPER_HOME=/some/folder`
runs a separate test setup beside the real one; `HESPER_DEVTOOLS=1` enables the pages' inspector
(right click → Inspect); `HESPER_THEME=dark|light` forces a look; `HESPER_NO_TRAY=1` tries the
window-only mode. The log is `~/.local/share/Hesper/hesper.log`.

**Trying it without installing Linux**: an Ubuntu USB stick (8 GB is enough), started in *Try
Ubuntu* mode on a PC, changes nothing on the PC's disk. Everything Hesper downloads lives in memory
then, so a PC with 16 GB of memory or more, and the CPU only (the NVIDIA driver isn't there in that
mode).

Known: on NVIDIA's driver, WebKitGTK may show blank windows — the AppImage sets
`WEBKIT_DISABLE_DMABUF_RENDERER=1` when it finds that driver.
