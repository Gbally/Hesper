# Builds Hesper for the Microsoft Store: an MSIX package to upload in Partner Center, which signs it,
# installs it and keeps it up to date. Inside, Hesper notices it is the Store's (Platform.Store) and
# leaves updates, its login item, its links and its removal to Windows; its data goes in the package's
# own folder, which Windows deletes with the app.
#
#   .\scripts\store_windows.ps1         the Store's package: build-cache\store\Hesper-X.msix, with the
#                                       identity Partner Center gave the app (win\Store\identity.json)
#   .\scripts\store_windows.ps1 -Test   the same with a test identity ("Hesper (test)"), to try on this
#                                       PC first; it says how to install it from its folder (that needs
#                                       Windows' Developer Mode) and how to remove it
#
# The version is pyproject.toml's, as X.Y.Z.0 (the Store keeps the last number). Needs the .NET 10 SDK
# (as release_windows.ps1) and the Windows SDK's makepri and makeappx: an installed Windows SDK's, else
# those of the Microsoft.Windows.SDK.BuildTools package, downloaded once into build-cache and checked.
param([switch]$Test)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"  # Windows PowerShell 5's progress bar slows downloads a lot
Set-Location (Join-Path $PSScriptRoot "..")
$Root = (Get-Location).Path
$Out = Join-Path $Root ("build-cache\store" + $(if ($Test) { "-test" } else { "" }))
$Layout = Join-Path $Out "Hesper"  # the package's files, as Windows installs them

# The Windows SDK build tools, when no Windows SDK is installed: nuget.org's package and its SHA-512
$BuildToolsVersion = "10.0.26100.9169"
$BuildToolsSha512 = "hOpNh7bCzJOOm2hvKuodr1NUKHeqoUhxdAzTmhO/q+u/E+5FOyhK+THhNhUUfKLbWnSslZ/m35dwghmS2BzaWg=="

# The portable .NET SDK first, with its caches next to it (as release_windows.ps1)
$Tools = Join-Path (Split-Path $Root) "tools"
$Dotnet = if (Test-Path "$Tools\dotnet\dotnet.exe") { "$Tools\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
if ($Dotnet -and $Dotnet.StartsWith($Tools)) {
    $env:DOTNET_ROOT = "$Tools\dotnet"; $env:DOTNET_CLI_HOME = "$Tools\dotnet-home"; $env:NUGET_PACKAGES = "$Tools\nuget"
    $env:DOTNET_NOLOGO = "1"; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
if (-not $Dotnet -or -not ((& $Dotnet --list-sdks) -match '^10\.')) { throw "The .NET 10 SDK isn't installed (dotnet, or MeetScribe\tools\dotnet)." }

# Who the app is to the Store: Partner Center → Apps and games → Hesper → Product management →
# Product identity (Package/Identity/Name, Package/Identity/Publisher, Package/Properties/PublisherDisplayName)
if ($Test) {
    $Id = [pscustomobject]@{ name = "Gbally.HesperTest"; publisher = "CN=Hesper Test"; publisherDisplayName = "Gbally"; displayName = "Hesper (test)" }
} else {
    if (git status --porcelain) { throw "Commit your changes first: the Store's package is built from committed code." }
    $Id = Get-Content win\Store\identity.json -Raw | ConvertFrom-Json
    if (-not $Id.name -or -not $Id.publisher -or -not $Id.publisherDisplayName -or -not $Id.displayName) {
        throw "Fill in win\Store\identity.json first, from Partner Center: Hesper → Product management → Product identity."
    }
}
$Version = (Select-String -Path pyproject.toml -Pattern '^version = "(.*)"').Matches[0].Groups[1].Value
$PackageVersion = "$Version.0"
$Msix = "Hesper-$Version" + $(if ($Test) { "-test" } else { "" }) + ".msix"

if (Test-Path $Out) {
    # A test package installed from this folder must be removed first (Windows runs it from here)
    if ($Test -and (Get-AppxPackage -Name $Id.name)) { throw "Remove the installed test package first: Get-AppxPackage $($Id.name) | Remove-AppxPackage" }
    Remove-Item -Recurse -Force $Out
}
New-Item -ItemType Directory -Force $Out | Out-Null

# 1. The app, as loose files (no single-file exe: Windows installs and updates the package's files)
Write-Host "- Building Hesper $Version for the Microsoft Store$(if ($Test) { ' (test identity)' })"
& $Dotnet publish win\Hesper -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -p:DebugType=none `
    -p:Version="$PackageVersion" -o $Layout | Select-String -Pattern "error|Hesper ->"
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

# 2. The Start menu, taskbar and Store images, drawn by Hesper itself (Brand.cs)
$p = Start-Process -FilePath "$Layout\Hesper.exe" -ArgumentList "--write-package-images", "`"$Layout\Assets`"" -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path "$Layout\Assets\StoreLogo.scale-100.png")) { throw "The package images couldn't be drawn." }

# 3. The package's description, with its identity and version
$Manifest = Get-Content win\Store\AppxManifest.xml -Raw -Encoding UTF8
$Slots = @{ Name = $Id.name; Publisher = $Id.publisher; PublisherDisplayName = $Id.publisherDisplayName; DisplayName = $Id.displayName; Version = $PackageVersion }
foreach ($k in $Slots.Keys) { $Manifest = $Manifest.Replace("{{$k}}", [Security.SecurityElement]::Escape([string]$Slots[$k])) }
if ($Manifest -match "{{") { throw "AppxManifest.xml has a slot this script doesn't fill." }
[IO.File]::WriteAllText("$Layout\AppxManifest.xml", $Manifest)  # UTF-8 without BOM

# 4. makepri (which image for which scale and size: resources.pri) and makeappx (the .msix)
function Find-SdkTool($name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $installed = Get-ChildItem $kits -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($installed) { return $installed.FullName }
    $dir = Join-Path $Root "build-cache\sdk-buildtools-$BuildToolsVersion"
    $exe = Join-Path $dir "bin\10.0.26100.0\x64\$name"
    if (-not (Test-Path $exe)) {
        Write-Host "- Downloading the Windows SDK build tools $BuildToolsVersion (23 MB, nuget.org) into build-cache"
        $zip = "$dir.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -OutFile $zip `
            "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$BuildToolsVersion/microsoft.windows.sdk.buildtools.$BuildToolsVersion.nupkg"
        $expected = [BitConverter]::ToString([Convert]::FromBase64String($BuildToolsSha512)).Replace("-", "")
        if ((Get-FileHash $zip -Algorithm SHA512).Hash -ne $expected) { Remove-Item $zip; throw "The Windows SDK build tools download isn't the expected file." }
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $dir)
        Remove-Item $zip
    }
    return $exe
}
$MakePri = Find-SdkTool "makepri.exe"
$MakeAppx = Find-SdkTool "makeappx.exe"
Write-Host "- Packaging with $(Split-Path $MakeAppx)"
$log = & $MakePri createconfig /cf "$Out\priconfig.xml" /dq en-US /pv 10.0.0 /o
if ($LASTEXITCODE -ne 0) { $log; throw "makepri createconfig failed." }
$log = & $MakePri new /pr $Layout /cf "$Out\priconfig.xml" /mn "$Layout\AppxManifest.xml" /of "$Layout\resources.pri" /o
if ($LASTEXITCODE -ne 0) { $log; throw "makepri failed." }
$log = & $MakeAppx pack /d $Layout /p "$Out\$Msix" /o
if ($LASTEXITCODE -ne 0) { $log; throw "makeappx failed." }

$Size = [math]::Round((Get-Item "$Out\$Msix").Length / 1MB, 1)
Write-Host ""
Write-Host "OK: $Out\$Msix ($Size MB), version $PackageVersion - nothing published."
if ($Test) {
    Write-Host "   To try it on this PC (Developer Mode on: Settings -> System -> For developers), quit the"
    Write-Host "   downloaded Hesper (one Hesper at a time), then:"
    Write-Host "     Add-AppxPackage -Register `"$Layout\AppxManifest.xml`""
    Write-Host "   It is 'Hesper (test)' in the Start menu, with its own engine, models and settings."
    Write-Host "   Remove it with:  Get-AppxPackage $($Id.name) | Remove-AppxPackage"
} else {
    Write-Host "   Upload it in Partner Center: Apps and games -> Hesper -> the submission -> Packages."
}
