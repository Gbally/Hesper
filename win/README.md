# Hesper for Windows (preview)

A tray app (C# / .NET 10, WinForms, NAudio) around the same Python engine as the Mac app.

Much of it is shared with the Linux app (`linux/`, see linux/README.md): the files that say so at
their top, and `Ui/`. Keep Windows-only APIs out of those (they go through `Platform.cs`).

- `Recorder.cs`: microphone + PC sound (WASAPI loopback) → `mic.wav` / `system.wav`, 16 kHz mono,
  one timeline. Windows' default devices (not the "communications" ones), or the ones chosen in Settings.
- `EngineRunner.cs`: `python -m hesper …` with Hesper's environment; reads `@@step` / `@@progress`.
- `Setup.cs`: first launch into `%LOCALAPPDATA%\Hesper`: the engine (inside the exe), uv, Python 3.12,
  `uv sync --frozen [--extra cuda]`, llama.cpp (CUDA or CPU build), `hesper setup` for the models.
  uv and llama.cpp are pinned and checked by SHA-256. No admin rights, nothing system-wide.
- `TrayApp.cs`, `SettingsForm.cs`, `Settings.cs` (settings.json → config.toml), `Brand.cs` (the icon).

Build, with the .NET 10 SDK (a portable one is fine):

```powershell
cd win\Hesper
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ..\dist
```

Tests without the tray: `Hesper.exe --record SECONDS [NOTES_FOLDER] [MIC_NAME]` records then makes the
notes; `Hesper.exe --devices` lists the default devices and how loud each one is (for support);
`Hesper.exe --write-icon Hesper.ico` redraws the icon. `HESPER_HOME` moves the data folder (a test setup).

Publishing, as `scripts/release.sh` for the Mac (the .NET 10 SDK and `gh auth login` once):

```powershell
.\scripts\release_windows.ps1 "What's new"     # prepare: build-cache\windows\Hesper-X.exe + latest-windows.json
.\scripts\release_windows.ps1 -Publish         # push main, the exe on release vX, then the feed + models.json
```

`-Beta` (with either) makes a test version for `latest-windows-beta.json`. The version is pyproject's,
the build number one more than the feeds'; it is the exe's 4th version number, which the updater
compares. The site's Windows button turns on with `latest-windows.json`. The exe isn't code-signed
yet: SmartScreen warns on the first download.

**Microsoft Store** (an MSIX package; the Store signs it, so no SmartScreen warning): the same app,
which notices it was installed from the Store (`Platform.Store`, it has a package identity) and then
has no updater, no "Remove Hesper" (Windows uninstalls it), and no Run key, Start menu shortcut,
app ID or `hesper://` registration of its own: `win/Store/AppxManifest.xml` declares them (the login
item is its startup task, `Startup` in `Services.cs`). Its data folder is the package's own
(`%LOCALAPPDATA%\Packages\<its name>\LocalCache`, deleted with the app), so it never shares an
engine or settings with the downloaded exe.

```powershell
.\scripts\store_windows.ps1 -Test     # build-cache\store-test: a "Hesper (test)" package to try on this PC
.\scripts\store_windows.ps1           # build-cache\store\Hesper-X.msix, to upload in Partner Center
.\scripts\store_screenshots.ps1       # build-cache\store-shots\en: the listing's pictures (-Lang fr…)
```

The package needs `win\Store\identity.json` filled in from Partner Center (Hesper → Product
management → Product identity) and committed code. Package version X.Y.Z.0 (the Store keeps the
4th number): each upload needs a higher version. The pictures are the app's own windows
(`--capture DIR SHOTS.json`) with the invented meetings of `win/Store/demo`, in a test data folder
(`HESPER_HOME` + `HESPER_DEMO=1`: counts as set up, reads nothing of the PC's Hesper).
`win/Store/LISTING.md` has every field Partner Center asks for, in the six languages. The exe
(`release_windows.ps1`) is unchanged by all this.

Diagnostics: `Hesper.exe --calendar` (what Hesper reads from Outlook), `--devices`, `--capture DIR`
(PNGs of every screen, light and dark), `--record SECONDS [FOLDER] [MIC] [process:NAME]`, `--notify`
(a test notification, and whether Windows lists Hesper in Settings → Notifications).

Recordings are written in `%LOCALAPPDATA%\Hesper\recordings` and move to the notes folder once
processed (a OneDrive folder never gets audio being written); one not processed (Hesper quit,
crashed, updated) is processed at the next launch. Hesper's app ID (`Gbally.Hesper`, with a
Start menu shortcut) is what Windows lists its notifications under. A test setup (`HESPER_HOME`) runs
beside the real Hesper; `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` lets a
script drive its pages.
