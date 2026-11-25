#!/usr/bin/env bash
# Writes THIRD_PARTY_LICENSES.txt: the full license texts of what ships INSIDE the apps (uv in the
# Mac app; the .NET runtime in the Windows app and the Linux AppImage; the WebView2 SDK and NAudio
# in the Windows app; the AppImage runtime). The apps carry this file and Settings → About opens it.
# The projects that are only downloaded (Python packages, models) keep their own license files; the
# full credits are in THIRD_PARTY_NOTICES.md.
#
#   scripts/licenses.sh        run again after changing .NET, WebView2, NAudio, uv or the AppImage
#                              runtime; then commit THIRD_PARTY_LICENSES.txt
#
# Reads the .NET and WebView2 texts from the restored NuGet packages (build-cache/nuget, as
# build-cache/dn.sh restores them: `dn.sh restore win/Hesper/Hesper.csproj -r win-x64
# -p:SelfContained=true -p:EnableWindowsTargeting=true`), the others from their repositories.
set -euo pipefail
cd "$(dirname "$0")/.."
NUGET=${NUGET_PACKAGES:-build-cache/nuget}
DOTNET=$(sed -n 's/.*<TargetFramework>net\([0-9]*\)\..*/\1/p' win/Hesper/Hesper.csproj)
RUNTIME=$(ls -d "$NUGET"/microsoft.netcore.app.runtime.win-x64/"$DOTNET".* 2>/dev/null | sort -V | tail -1)
WEBVIEW=$(sed -n 's/.*"Microsoft.Web.WebView2" Version="\([^"]*\)".*/\1/p' win/Hesper/Hesper.csproj)
NAUDIO=$(sed -n 's/.*"NAudio.Wasapi" Version="\([^"]*\)".*/\1/p' win/Hesper/Hesper.csproj)
APPIMAGE_RUNTIME=$(sed -n 's|.*type2-runtime/releases/download/\([0-9]*\)/.*|\1|p' linux/build_appimage.sh | head -1)
[ -n "$RUNTIME" ] && [ -f "$RUNTIME/THIRD-PARTY-NOTICES.TXT" ] || { echo "Restore the Windows app for win-x64 first (see the top of this script)."; exit 1; }
[ -f "$NUGET/microsoft.web.webview2/$WEBVIEW/LICENSE.txt" ] || { echo "WebView2 $WEBVIEW isn't restored."; exit 1; }

get() { curl -fsSL "$1" | tr -d '\r'; }
section() {  # title, where it ships, source
  printf '\n\n%s\n%s\n%s\nSource: %s\n\n' "================================================================================" "$1" "$2" "$3"
}
{
  echo "Hesper — licenses of the third-party software inside the app"
  echo
  echo "Hesper is free software under the MIT License (below). The Mac app, the Windows app and the"
  echo "Linux AppImage carry the projects listed here, under their own licenses. Everything else Hesper"
  echo "uses (the Python engine's packages, llama.cpp, the AI models) is downloaded from its publisher"
  echo "with its own license files; the full list of credits, with links, is at"
  echo "https://github.com/Gbally/Hesper/blob/main/THIRD_PARTY_NOTICES.md"

  section "Hesper" "All apps" "https://github.com/Gbally/Hesper"
  tr -d '\r' < LICENSE

  section "uv" "Inside the Mac app (offered under MIT or Apache 2.0; used here under MIT)" "https://github.com/astral-sh/uv"
  get https://raw.githubusercontent.com/astral-sh/uv/main/LICENSE-MIT

  section ".NET runtime and Windows Desktop runtime ${RUNTIME##*/}" "Inside the Windows app and the Linux AppImage" "https://github.com/dotnet/runtime"
  tr -d '\r' < "$RUNTIME/LICENSE.TXT"; echo; echo
  tr -d '\r' < "$RUNTIME/THIRD-PARTY-NOTICES.TXT"

  section "Microsoft Edge WebView2 SDK $WEBVIEW" "Inside the Windows app" "https://aka.ms/webview"
  tr -d '\r' < "$NUGET/microsoft.web.webview2/$WEBVIEW/LICENSE.txt"

  section "NAudio $NAUDIO" "Inside the Windows app" "https://github.com/naudio/NAudio"
  get https://raw.githubusercontent.com/naudio/NAudio/master/license.txt

  section "AppImage runtime (type2-runtime $APPIMAGE_RUNTIME)" "At the start of the Linux AppImage" "https://github.com/AppImage/type2-runtime"
  get https://raw.githubusercontent.com/AppImage/type2-runtime/main/LICENSE
  echo
  echo
  echo "The AppImage runtime is a static program built on Alpine Linux. It includes these libraries,"
  echo "under their own licenses. Its complete source and build scripts, which rebuild it with modified"
  echo "versions of them, are at https://github.com/AppImage/type2-runtime/tree/$APPIMAGE_RUNTIME"
  echo "- libfuse 3: GNU LGPL 2.1 — https://github.com/libfuse/libfuse"
  echo "- squashfuse: BSD 2-Clause — https://github.com/vasi/squashfuse"
  echo "- zstd: BSD 3-Clause — https://github.com/facebook/zstd"
  echo "- zlib: zlib License — https://zlib.net"
  echo "- mimalloc: MIT — https://github.com/microsoft/mimalloc"
  echo "- musl libc: MIT — https://musl.libc.org"
} > THIRD_PARTY_LICENSES.txt
echo "OK: THIRD_PARTY_LICENSES.txt ($(wc -c < THIRD_PARTY_LICENSES.txt | tr -d ' ') bytes) — commit it."
