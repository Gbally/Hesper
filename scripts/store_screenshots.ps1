# Pictures for the Microsoft Store listing (Partner Center -> Store listings), made on the Windows PC:
# Hesper's own windows, drawn by the app as users see them (WebView2), with the invented meetings of
# win\Store\demo (no real meeting, name or voice), set on a background at 1920x1080 (times the
# screen's scale). Also the 300x300 tile icon and a hero picture (no text, as the Store asks).
#
#   .\scripts\store_screenshots.ps1            the app in English
#   .\scripts\store_screenshots.ps1 -Lang fr   the app in French (the invented meetings stay in English)
#
# Output: build-cache\store-shots\<lang>\ (01-notes.png ...). The app runs with a test data folder
# (HESPER_HOME, in build-cache) and HESPER_DEMO=1: it never reads this PC's settings, meetings,
# voices or calendar, and the real Hesper may stay open. Needs the .NET 10 SDK (as release_windows.ps1).
param([ValidateSet("en", "fr", "es", "pt", "ja", "zh-Hans")][string]$Lang = "en")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$Root = (Get-Location).Path
$Work = Join-Path $Root "build-cache\store-shots"
$App = Join-Path $Work "app"
$DemoHome = Join-Path $Work "home"
$Notes = Join-Path $Work "MeetingNotes"
$Raw = Join-Path $Work "raw-$Lang"
$Out = Join-Path $Work $Lang

# The portable .NET SDK first, with its caches next to it (as release_windows.ps1)
$Tools = Join-Path (Split-Path $Root) "tools"
$Dotnet = if (Test-Path "$Tools\dotnet\dotnet.exe") { "$Tools\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
if ($Dotnet -and $Dotnet.StartsWith($Tools)) {
    $env:DOTNET_ROOT = "$Tools\dotnet"; $env:DOTNET_CLI_HOME = "$Tools\dotnet-home"; $env:NUGET_PACKAGES = "$Tools\nuget"
    $env:DOTNET_NOLOGO = "1"; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
if (-not $Dotnet -or -not ((& $Dotnet --list-sdks) -match '^10\.')) { throw "The .NET 10 SDK isn't installed (dotnet, or MeetScribe\tools\dotnet)." }

# 1. The app (loose files, in build-cache)
Write-Host "- Building Hesper"
& $Dotnet publish win\Hesper -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -p:DebugType=none -o $App | Select-String -Pattern "error|Hesper ->"
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

# 2. The invented meetings, moved in time so that the client call is today (the demo is set in the
#    week of Monday 23 November 2026), and the test data folder's settings
foreach ($d in @($DemoHome, $Notes, $Raw, $Out)) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
New-Item -ItemType Directory -Force $DemoHome, $Notes, $Out | Out-Null
$Days = ((Get-Date).Date - [datetime]"2026-11-23").Days
foreach ($meeting in Get-ChildItem win\Store\demo -Directory) {
    New-Item -ItemType Directory -Force (Join-Path $Notes $meeting.Name) | Out-Null
    foreach ($f in Get-ChildItem $meeting.FullName -File) {
        $text = [IO.File]::ReadAllText($f.FullName)
        $text = [regex]::Replace($text, '(?<=")\d{4}-\d{2}-\d{2}', { param($m) ([datetime]::ParseExact($m.Value, "yyyy-MM-dd", $null)).AddDays($Days).ToString("yyyy-MM-dd") })
        [IO.File]::WriteAllText((Join-Path (Join-Path $Notes $meeting.Name) $f.Name), $text)
    }
}
$Settings = [ordered]@{
    NotesFolder = $Notes; Storage = "custom"; CustomFolderPath = $Notes; Welcomed = $true; MyName = "Alex Martin"; AppLanguage = $Lang
    UseCalendar = $false; CalendarLink = ""; AutoRecord = $false; Template = "client"; Hotkey = "Ctrl+Alt+R"
}
[IO.File]::WriteAllText((Join-Path $DemoHome "settings.json"), ($Settings | ConvertTo-Json))

# 3. The shots: a page, its size, light or dark, and what to show in it (JavaScript run in the page)
$Clean = 'state.betterModel = null; state.laterPending = false; state.memory = null; state.job = null; state.answer = null;'
$Recording = $Clean + @'
state.recording = { elapsed: 1384, mic: 0.2, system: 0.34, notices: [], id: "demo-recording", template: null, asking: null, paused: false,
  event: { title: "Helios Energy: contract terms", end: new Date(Date.now() + 26 * 60000).toISOString() },
  notes: "Their legal team wants the liability cap at 1x annual fees\n[ ] send the redlined contract to Laura" };
drawContent(); drawLive();
'@
$Ask = $Clean + @'
state.answer = { question: "What did we promise Northwind?", result: {
  answer: "A revised quote and statement of work in two phases by Wednesday (Lisa) [1]. Go-live on December 1st without single sign-on, with single sign-on in January [1]. Alex Martin confirms by Friday that backups can move to the EU [1].",
  sources: [{ n: 1, id: "northwind-renewal", title: "Q4 renewal with Northwind", date: new Date().toLocaleDateString(locale(), { day: "numeric", month: "short" }) }] } };
drawContent();
'@
$Shots = @(
    @{ name = "today"; page = "home"; width = 400; height = 600; popover = $true; script = $Clean + 'tab = "today"; drawContent(); drawTabs();' }
    # taller than the popover (as the resizable home window can be): the recording card fills 600 points,
    # and this morning's meeting should show below it
    @{ name = "recording"; page = "home"; width = 400; height = 720; popover = $true; script = $Recording }
    @{ name = "todos"; page = "home"; width = 400; height = 600; popover = $true; script = $Clean + 'tab = "todos"; drawContent(); drawTabs();' }
    @{ name = "ask"; page = "home"; width = 400; height = 600; popover = $true; script = $Ask }
    @{ name = "meeting"; page = "meeting/northwind-renewal"; width = 760; height = 900 }
    @{ name = "transcript"; page = "meeting/northwind-renewal"; width = 760; height = 900; script = 'view = "transcript"; renderMeeting();' }
    @{ name = "meeting-dark"; page = "meeting/northwind-renewal"; width = 760; height = 900; dark = $true }
    @{ name = "today-dark"; page = "home"; width = 400; height = 600; popover = $true; dark = $true; script = $Clean + 'tab = "today"; drawContent(); drawTabs();' }
)
$ShotsFile = Join-Path $Work "shots.json"
[IO.File]::WriteAllText($ShotsFile, (ConvertTo-Json @($Shots) -Depth 4))

# 4. The pictures of the windows, by the app (a test data folder: the PC's own Hesper isn't touched)
Write-Host "- Drawing Hesper's windows ($Lang)"
$env:HESPER_HOME = $DemoHome; $env:HESPER_DEMO = "1"
try { Start-Process -FilePath "$App\Hesper.exe" -ArgumentList "--capture", "`"$Raw`"", "`"$ShotsFile`"" -Wait }
finally { Remove-Item Env:HESPER_HOME, Env:HESPER_DEMO -ErrorAction SilentlyContinue }
if (Test-Path "$Raw\capture-error.txt") { Get-Content "$Raw\capture-error.txt"; throw "The windows couldn't be drawn." }
foreach ($s in $Shots) { if (-not (Test-Path "$Raw\$($s.name).png")) { throw "No picture of $($s.name)." } }

# 5. Each Store picture: the windows on Hesper's evening-sky background, with rounded corners and a shadow
Add-Type -AssemblyName System.Drawing
$Scale = [Drawing.Image]::FromFile("$Raw\meeting.png").Width / 760.0  # the screen's scale (1 at 100 %)

function New-Rounded([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"; $g.PixelOffsetMode = "HighQuality"
    $sky = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $h),
        ([Drawing.ColorTranslator]::FromHtml("#3A4470")), ([Drawing.ColorTranslator]::FromHtml("#151926"))
    $g.FillRectangle($sky, 0, 0, $w, $h)
    # The evening star's glow, top right
    $glow = New-Object Drawing.Drawing2D.GraphicsPath
    $glow.AddEllipse([float]($w * 0.62), [float](-$h * 0.45), [float]($w * 0.7), [float]($h * 0.9))
    $warm = New-Object Drawing.Drawing2D.PathGradientBrush $glow
    $warm.CenterColor = [Drawing.Color]::FromArgb(70, 242, 181, 68); $warm.SurroundColors = @([Drawing.Color]::FromArgb(0, 242, 181, 68))
    $g.FillPath($warm, $glow)
    return @($bmp, $g)
}

# layers: @(name, x, y) in 1920x1080 points; each window keeps its own pixels (no resizing)
function Compose([string]$file, [object[]]$layers) {
    $bmp, $g = New-Canvas ([int](1920 * $Scale)) ([int](1080 * $Scale))
    foreach ($l in $layers) {
        $img = [Drawing.Image]::FromFile("$Raw\$($l[0]).png")
        $x = [float]($l[1] * $Scale); $y = [float]($l[2] * $Scale); $w = [float]$img.Width; $h = [float]$img.Height; $r = [float](8 * $Scale)
        for ($i = 1; $i -le 24; $i++) {  # a soft shadow, below
            $k = [float]($i * $Scale)
            $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(6, 0, 0, 0))), (New-Rounded ($x - $k) ($y - $k + 18 * $Scale) ($w + 2 * $k) ($h + 2 * $k) ($r + $k)))
        }
        $tex = New-Object Drawing.TextureBrush $img, ([Drawing.Drawing2D.WrapMode]::Clamp)
        $tex.TranslateTransform($x, $y)
        $g.FillPath($tex, (New-Rounded $x $y $w $h $r))
        $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(48, 255, 255, 255)), ([float]$Scale)), (New-Rounded $x $y $w $h $r))
        $tex.Dispose(); $img.Dispose()
    }
    $bmp.Save("$Out\$file", [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

Write-Host "- Composing the Store's pictures"
Compose "01-notes.png" @(@("meeting", 360, 90), @("today", 1160, 390))
Compose "02-recording.png" @(@("recording", 500, 180), @("todos", 1020, 240))
Compose "03-transcript.png" @(@("transcript", 360, 90), @("ask", 1160, 390))
Compose "04-dark.png" @(@("meeting-dark", 360, 90), @("today-dark", 1160, 390))

# 6. The 300x300 tile icon (Store logos) and the hero picture (no text), from the app's icon
$Icons = Join-Path $Work "icons"
if (Test-Path $Icons) { Remove-Item -Recurse -Force $Icons }
Start-Process -FilePath "$App\Hesper.exe" -ArgumentList "--write-package-images", "`"$Icons`"" -Wait
Copy-Item "$Icons\Square150x150Logo.scale-200.png" "$Out\tile-300x300.png"
$bmp, $g = New-Canvas 1920 1080
$icon = [Drawing.Image]::FromFile("$Icons\Square150x150Logo.scale-400.png")
$g.DrawImage($icon, [float](960 - 180), [float](540 - 180), [float]360, [float]360)
$bmp.Save("$Out\hero-1920x1080.png", [Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose(); $g.Dispose(); $bmp.Dispose()

Write-Host ""
Write-Host "OK: $Out"
Get-ChildItem $Out | ForEach-Object { Write-Host ("   {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB)) }
